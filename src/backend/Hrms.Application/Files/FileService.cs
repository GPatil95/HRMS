using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Hrms.Application.Abstractions;
using Hrms.Application.Auditing;
using Hrms.Application.Authorization;
using Hrms.Application.Modules;
using Hrms.Domain.Tenant;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Files;

public sealed record FileUploadRequest(string Purpose, string FileName, string? ContentType, Stream Content, long Length, string? OwnerEntityType, string? OwnerEntityId);

public sealed record FileMetadataDto(Guid Id, string FileName, string ContentType, long SizeBytes, string Purpose, string Sha256, FileStatus Status, DateTimeOffset CreatedAt);

public sealed record FileDownload(string FileName, string ContentType, Stream Content);

/// <summary>
/// Tenant-aware file service used by every module. Enforces purpose-based permissions, size/type/signature validation,
/// tenant-prefixed storage keys and download audit. Business modules never touch <see cref="IFileStorageService"/> directly.
/// </summary>
public sealed partial class FileService(
    ITenantDbContext db,
    ITenantContextAccessor tenant,
    ICurrentUser user,
    IRequestContext request,
    IModuleCatalog catalog,
    IAccessGuard access,
    IFileStorageService storage,
    IMalwareScanner scanner,
    IAuditWriter audit,
    IClock clock)
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    [GeneratedRegex(@"[^A-Za-z0-9._ -]")]
    private static partial Regex UnsafeChars();

    public async Task<Result<FileMetadataDto>> UploadAsync(FileUploadRequest req, CancellationToken ct)
    {
        var purpose = catalog.Modules.SelectMany(m => m.FilePurposes).FirstOrDefault(p => p.Code == req.Purpose);
        if (purpose is null)
        {
            return Error.Validation("invalid-purpose", "Unknown file purpose.");
        }

        if (await access.CheckAsync(purpose.ModuleCode, purpose.UploadPermission, ct) is { } denied)
        {
            return denied;
        }

        var safeName = SanitizeFileName(req.FileName);
        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        if (!purpose.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) || !ContentTypes.TryGetValue(ext, out var contentType))
        {
            return Error.Validation("file-type-not-allowed", "This file type is not allowed.");
        }

        if (req.Length <= 0 || req.Length > purpose.MaxBytes)
        {
            return Error.Validation("file-size-invalid", $"File must be between 1 byte and {purpose.MaxBytes / (1024 * 1024)} MB.");
        }

        await using var buffer = new MemoryStream();
        await CopyLimitedAsync(req.Content, buffer, purpose.MaxBytes, ct);
        if (buffer.Length == 0 || buffer.Length > purpose.MaxBytes)
        {
            return Error.Validation("file-size-invalid", "File size is invalid.");
        }

        var bytes = buffer.ToArray();
        if (!FileSignatures.Matches(ext, bytes))
        {
            return Error.Validation("file-signature-mismatch", "File content does not match its extension.");
        }

        buffer.Position = 0;
        var scan = await scanner.ScanAsync(buffer, ct);
        if (!scan.IsClean)
        {
            return Error.Validation("file-rejected", "File failed the security scan.");
        }

        var t = tenant.Required;
        var file = new FileObject
        {
            OriginalFileName = safeName,
            ContentType = contentType,
            SizeBytes = bytes.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Purpose = purpose.Code,
            Classification = purpose.Classification,
            OwnerEntityType = req.OwnerEntityType,
            OwnerEntityId = req.OwnerEntityId,
            Status = FileStatus.Available,
        };
        var now = clock.UtcNow;
        file.StorageKey = $"t/{t.TenantId:N}/{purpose.Code}/{now:yyyy}/{now:MM}/{file.Id:N}{ext}";
        buffer.Position = 0;
        await storage.UploadAsync(file.StorageKey, buffer, contentType, ct);
        db.Files.Add(file);
        db.FileAccessLogs.Add(new FileAccessLog { FileId = file.Id, UserId = user.UserId, Action = "upload", OccurredAt = now, IpAddress = request.IpAddress });
        await db.SaveChangesAsync(ct);
        return Map(file);
    }

    public async Task<Result<FileMetadataDto>> GetMetadataAsync(Guid fileId, CancellationToken ct)
    {
        var (file, error) = await LoadAuthorizedAsync(fileId, p => p.DownloadPermission, ct);
        return error is not null ? error : Map(file!);
    }

    public async Task<Result<bool>> ExistsAsync(Guid fileId, CancellationToken ct)
    {
        var (file, error) = await LoadAuthorizedAsync(fileId, p => p.DownloadPermission, ct);
        if (error is not null)
        {
            return error.Type == ErrorType.NotFound ? false : error;
        }

        return await storage.ExistsAsync(file!.StorageKey, ct);
    }

    public async Task<Result<FileDownload>> DownloadAsync(Guid fileId, CancellationToken ct)
    {
        var (file, error) = await LoadAuthorizedAsync(fileId, p => p.DownloadPermission, ct);
        if (error is not null)
        {
            return error;
        }

        var stream = await storage.OpenReadAsync(file!.StorageKey, ct);
        db.FileAccessLogs.Add(new FileAccessLog { FileId = file.Id, UserId = user.UserId, Action = "download", OccurredAt = clock.UtcNow, IpAddress = request.IpAddress });
        audit.Add(FilesModule.ModuleCode, nameof(FileObject), file.Id.ToString(), "download", new { file.Purpose, file.SizeBytes });
        await db.SaveChangesAsync(ct);
        return new FileDownload(file.OriginalFileName, file.ContentType, stream);
    }

    public async Task<Result> DeleteAsync(Guid fileId, CancellationToken ct)
    {
        var (file, error) = await LoadAuthorizedAsync(fileId, p => p.DeletePermission, ct);
        if (error is not null)
        {
            return error;
        }

        file!.IsDeleted = true;
        file.DeletedAt = clock.UtcNow;
        file.DeletedBy = user.UserId;
        db.FileAccessLogs.Add(new FileAccessLog { FileId = file.Id, UserId = user.UserId, Action = "delete", OccurredAt = clock.UtcNow, IpAddress = request.IpAddress });
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(file.StorageKey, ct);
        return Result.Success();
    }

    private async Task<(FileObject? File, Error? Error)> LoadAuthorizedAsync(Guid fileId, Func<FilePurposeDefinition, string> permission, CancellationToken ct)
    {
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId && !f.IsDeleted, ct);
        var expectedPrefix = $"t/{tenant.Required.TenantId:N}/";
        if (file is null || !file.StorageKey.StartsWith(expectedPrefix, StringComparison.Ordinal) || file.Status != FileStatus.Available)
        {
            return (null, Error.NotFound("file-not-found", "File not found."));
        }

        var purpose = catalog.Modules.SelectMany(m => m.FilePurposes).FirstOrDefault(p => p.Code == file.Purpose);
        if (purpose is null)
        {
            return (null, Error.NotFound("file-not-found", "File not found."));
        }

        return await access.CheckAsync(purpose.ModuleCode, permission(purpose), ct) is { } denied ? (null, denied) : (file, null);
    }

    public static string SanitizeFileName(string? name)
    {
        var baseName = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        baseName = UnsafeChars().Replace(baseName, "_").Trim(' ', '.');
        if (baseName.Length == 0)
        {
            baseName = "file";
        }

        return baseName.Length > 200 ? baseName[^200..] : baseName;
    }

    private static async Task CopyLimitedAsync(Stream source, Stream target, long max, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > max)
            {
                target.SetLength(max + 1);
                return;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static FileMetadataDto Map(FileObject f) => new(f.Id, f.OriginalFileName, f.ContentType, f.SizeBytes, f.Purpose, f.Sha256, f.Status, f.CreatedAt);
}

public static class FileSignatures
{
    public static bool Matches(string ext, ReadOnlySpan<byte> data) => ext switch
    {
        ".pdf" => data.StartsWith("%PDF-"u8),
        ".png" => data.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".jpg" or ".jpeg" => data.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        ".docx" or ".xlsx" => data.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 }),
        ".txt" or ".csv" => !data.Contains((byte)0),
        _ => false,
    };
}
