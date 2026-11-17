namespace Hrms.Application.Abstractions;

public sealed class HostingOptions
{
    public const string Section = "Hosting";
    /// <summary>Base domain for tenant subdomains, e.g. hrms.com (dev: localhost).</summary>
    public string BaseDomain { get; set; } = "localhost";
    /// <summary>Hosts that serve the platform admin realm.</summary>
    public string[] AdminHosts { get; set; } = ["admin.localhost"];
    /// <summary>Scheme + optional port used in links sent by email (e.g. https, http://...:5173).</summary>
    public string PublicScheme { get; set; } = "http";
    public int? PublicPort { get; set; } = 5173;

    public string BuildUrl(string host, string pathAndQuery) =>
        PublicPort is { } port ? $"{PublicScheme}://{host}:{port}{pathAndQuery}" : $"{PublicScheme}://{host}{pathAndQuery}";
}

public sealed class IdentityOptions
{
    public const string Section = "Identity";
    public int PasswordMinLength { get; set; } = 12;
    public bool RequireDigit { get; set; } = true;
    public bool RequireLetter { get; set; } = true;
    public int MaxFailedAccessAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int SessionIdleMinutes { get; set; } = 30;
    public int SessionAbsoluteHours { get; set; } = 12;
    public int PasswordResetTokenMinutes { get; set; } = 30;
    public int InvitationTokenHours { get; set; } = 72;
    public int EmailVerificationTokenHours { get; set; } = 24;
    public string MfaIssuer { get; set; } = "HRMS";
    public int LoginRateLimitPerIpPerMinute { get; set; } = 20;
    public int LoginRateLimitPerAccountPerMinute { get; set; } = 10;
}

public sealed class ProvisioningOptions
{
    public const string Section = "Provisioning";
    public string DefaultPlanCode { get; set; } = "foundation";
    public string SslMode { get; set; } = "Prefer";
    public int MaxPoolSize { get; set; } = 10;
}

public sealed class FileStorageOptions
{
    public const string Section = "FileStorage";
    public string Provider { get; set; } = "Local";
    public string LocalRootPath { get; set; } = "./.data/files";
    public string? S3Bucket { get; set; }
    public string? S3ServiceUrl { get; set; }
    public string? S3Region { get; set; }
    public bool S3ForcePathStyle { get; set; } = true;
}
