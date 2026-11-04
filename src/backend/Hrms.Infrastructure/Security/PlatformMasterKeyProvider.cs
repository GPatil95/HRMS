using System.Security.Cryptography;
using Hrms.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hrms.Infrastructure.Security;

/// <summary>
/// Single source of truth for the platform master key. Resolution order:
/// <list type="number">
/// <item><c>Secrets:MasterKey</c> — inline base64 (secret-manager injection).</item>
/// <item><c>Secrets:MasterKeyFile</c> — path to a file holding the base64 key; the file must already exist,
/// it is never auto-generated (first-time setup is the explicit <c>initialize-master-key</c> CLI command).</item>
/// <item><c>/keys/master.key</c> — container convention: the persistent keydata volume mount.</item>
/// <item>Development only: <c>~/.hrms-dev-masterkey</c>, created once on first use.</item>
/// </list>
/// Setting both <c>MasterKey</c> and <c>MasterKeyFile</c> is a hard configuration error — there is never a
/// silent pick. The key itself is never logged; a short SHA-256 fingerprint is.
/// </summary>
public sealed class PlatformMasterKeyProvider(
    IOptions<SecretStoreOptions> options,
    ILogger<PlatformMasterKeyProvider>? logger = null) : IPlatformMasterKeyProvider
{
    /// <summary>Container convention — the persistent keydata volume mounts here.</summary>
    public const string ContainerKeyPath = "/keys/master.key";

    private readonly object _gate = new();
    private MasterKeyInfo? _cached;

    public MasterKeyInfo Key
    {
        get { lock (_gate) { return _cached ??= Resolve(); } }
    }

    private MasterKeyInfo Resolve()
    {
        var o = options.Value;
        var inline = o.MasterKey?.Trim();
        var file = o.MasterKeyFile?.Trim();
        if (!string.IsNullOrEmpty(inline) && !string.IsNullOrEmpty(file))
        {
            throw new MasterKeyException(
                "Both Secrets:MasterKey and Secrets:MasterKeyFile are configured. " +
                "Use exactly one authoritative master-key source.");
        }

        string b64;
        string source;
        if (!string.IsNullOrEmpty(inline))
        {
            b64 = inline;
            source = "Secrets:MasterKey";
        }
        else
        {
            var path = ResolveKeyFilePath(file, out var mayCreate);
            if (!File.Exists(path))
            {
                if (!mayCreate)
                {
                    throw new MasterKeyException(
                        $"Master key file '{path}' does not exist. Run `initialize-master-key` once, " +
                        "or mount the persistent key volume (hrms_keydata) at /keys.");
                }

                CreateKeyFile(path);
            }

            b64 = File.ReadAllText(path).Trim();
            source = path;
        }

        byte[] key;
        try { key = Convert.FromBase64String(b64); }
        catch (FormatException)
        {
            throw new MasterKeyException($"Master key from {source} is not valid base64.");
        }

        if (key.Length != 32)
        {
            throw new MasterKeyException($"Master key from {source} must be 32 bytes (base64); got {key.Length} bytes.");
        }

        var fingerprint = Fingerprint(key);
        (logger ?? NullLogger<PlatformMasterKeyProvider>.Instance)
            .LogInformation("Platform master key loaded — source {Source}, fingerprint {Fingerprint}", source, fingerprint);
        return new MasterKeyInfo(key, source, fingerprint);
    }

    /// <summary>Short SHA-256 fingerprint — safe to log and compare between API and CLI.</summary>
    public static string Fingerprint(byte[] key) =>
        Convert.ToHexString(SHA256.HashData(key))[..12].ToLowerInvariant();

    /// <summary>The file <see cref="InitializeKeyFile"/> should create/read for this configuration.</summary>
    public static string ResolveKeyFileTarget(IConfiguration cfg)
    {
        var file = cfg[$"{SecretStoreOptions.Section}:MasterKeyFile"]?.Trim();
        if (!string.IsNullOrEmpty(file))
        {
            return file;
        }

        if (File.Exists(ContainerKeyPath) || Directory.Exists("/keys"))
        {
            return ContainerKeyPath;
        }

        return DevFallbackPath();
    }

    /// <summary>Creates a key file idempotently. Returns true when a new key was generated.</summary>
    public static bool InitializeKeyFile(string path)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return false;
        }

        CreateKeyFile(path);
        return true;
    }

    private static string ResolveKeyFilePath(string? configured, out bool mayCreate)
    {
        if (!string.IsNullOrEmpty(configured))
        {
            mayCreate = false;
            return configured;
        }

        if (File.Exists(ContainerKeyPath))
        {
            mayCreate = false;
            return ContainerKeyPath;
        }

        if (IsDevelopment())
        {
            mayCreate = true; // first-run dev convenience — file persists after creation, it is never re-generated
            return DevFallbackPath();
        }

        mayCreate = false;
        return configured ?? ContainerKeyPath;
    }

    private static void CreateKeyFile(string path)
    {
        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, generated);
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (IOException) { /* best effort on filesystems without unix modes */ }
        }
    }

    private static string DevFallbackPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hrms-dev-masterkey");

    private static bool IsDevelopment() =>
        string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            "Development", StringComparison.OrdinalIgnoreCase);
}
