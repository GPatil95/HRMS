namespace Hrms.Shared.Auditing;

public static class AuditSources
{
    public const string Web = "web";
    public const string Mobile = "mobile";
    public const string Api = "api";
    public const string Import = "import";
    public const string BackgroundJob = "job";
    public const string Integration = "integration";
    public const string Ai = "ai";
    public const string System = "system";
    public const string Support = "support";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>([Web, Mobile, Api, Import, BackgroundJob, Integration, Ai, System, Support]);
}

public static class ActorTypes
{
    public const string User = "user";
    public const string PlatformUser = "platform_user";
    public const string Service = "service";
    public const string Job = "job";
    public const string Anonymous = "anonymous";
}

public static class AuditActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
}
