using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Hrms.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Hrms.Infrastructure.Storage;

/// <summary>Local disk storage for development/tests. Keys are validated so they can never escape the root.</summary>
public sealed class LocalFileStorageService(IOptions<FileStorageOptions> options) : IFileStorageService
{
    private string Root => Path.GetFullPath(options.Value.LocalRootPath);

    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
        Task.FromResult<Stream>(new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read));

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(File.Exists(PathFor(key)));

    public Task<StoredFileMetadata?> GetMetadataAsync(string key, CancellationToken ct)
    {
        var fi = new FileInfo(PathFor(key));
        return Task.FromResult(fi.Exists ? new StoredFileMetadata(key, fi.Length, null, fi.LastWriteTimeUtc) : null);
    }

    private string PathFor(string key)
    {
        if (key.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(key))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        var full = Path.GetFullPath(Path.Combine(Root, key));
        return full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : throw new ArgumentException("Invalid storage key.", nameof(key));
    }
}

/// <summary>S3 / S3-compatible (MinIO) storage with SSE.</summary>
public sealed class S3FileStorageService(IAmazonS3 s3, IOptions<FileStorageOptions> options) : IFileStorageService
{
    private string Bucket => options.Value.S3Bucket ?? throw new InvalidOperationException("FileStorage:S3Bucket is not configured.");

    public Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct) =>
        s3.PutObjectAsync(new PutObjectRequest { BucketName = Bucket, Key = key, InputStream = content, ContentType = contentType, ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256 }, ct);

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct) => (await s3.GetObjectAsync(Bucket, key, ct)).ResponseStream;

    public Task DeleteAsync(string key, CancellationToken ct) => s3.DeleteObjectAsync(Bucket, key, ct);

    public async Task<bool> ExistsAsync(string key, CancellationToken ct) => await GetMetadataAsync(key, ct) is not null;

    public async Task<StoredFileMetadata?> GetMetadataAsync(string key, CancellationToken ct)
    {
        try
        {
            var m = await s3.GetObjectMetadataAsync(Bucket, key, ct);
            return new StoredFileMetadata(key, m.ContentLength, m.Headers.ContentType, m.LastModified);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}

/// <summary>Placeholder scanner: rejects the EICAR test signature. Replace with ClamAV/GuardDuty integration in production.</summary>
public sealed class SignatureMalwareScanner : IMalwareScanner
{
    private static readonly byte[] Eicar = Encoding.ASCII.GetBytes("EICAR-STANDARD-ANTIVIRUS-TEST-FILE");

    public async Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        content.Position = 0;
        return ms.ToArray().AsSpan().IndexOf(Eicar) >= 0 ? new MalwareScanResult(false, "EICAR-Test-File") : new MalwareScanResult(true, null);
    }
}
