namespace Hrms.Shared.Security;

public static class Realms
{
    public const string Tenant = "tenant";
    public const string Platform = "platform";
}

public static class HrmsClaims
{
    public const string Subject = "sub";
    public const string TenantId = "tid";
    public const string SessionId = "sid";
    public const string Realm = "realm";
    public const string SecurityStamp = "sstamp";
    public const string AuthMethods = "amr";
    public const string DisplayName = "name";
    public const string Email = "email";
}

/// <summary>Data scope levels. Order inside a chain matters for grant ceiling checks.</summary>
public enum ScopeLevel
{
    Own = 1,
    DirectReports = 2,
    AllReports = 3,
    Department = 4,
    Branch = 5,
    Company = 6,
    SelectedEmployees = 7,
    Tenant = 8,
}

public enum PermissionEffect
{
    Grant = 1,
    Deny = 2,
}

public static class PermissionActions
{
    public const string View = "view";
    public const string Create = "create";
    public const string Edit = "edit";
    public const string Delete = "delete";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Import = "import";
    public const string Export = "export";
    public const string Download = "download";
    public const string Process = "process";
    public const string Finalize = "finalize";
    public const string Configure = "configure";
    public const string Share = "share";
    public const string Access = "access";

    public static readonly IReadOnlySet<string> Standard = new HashSet<string>(
        [View, Create, Edit, Delete, Approve, Reject, Import, Export, Download, Process, Finalize, Configure, Share, Access]);
}

public enum PermissionKind
{
    Module = 1,
    Menu = 2,
    Feature = 3,
    Action = 4,
}
