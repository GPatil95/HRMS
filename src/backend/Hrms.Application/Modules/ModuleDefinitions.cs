using Hrms.Domain.Platform;
using Hrms.Shared.Security;

namespace Hrms.Application.Modules;

public sealed record PermissionDefinition(
    string Code,
    string ModuleCode,
    PermissionKind Kind,
    string Action,
    string Description,
    bool SupportsScope = false,
    bool IsSensitive = false);

public sealed record MenuItemDefinition(
    string Code,
    string Title,
    string? Route,
    string? Icon,
    int Order,
    string ModuleCode,
    string? ParentCode = null,
    IReadOnlyList<string>? AnyPermissions = null,
    string? FeatureCode = null,
    bool Visible = true);

public sealed record FeatureDefinition(string Code, string ModuleCode, string Name, string ValueType = "boolean");

public sealed record WidgetDefinition(
    string Code,
    string Title,
    string ModuleCode,
    string Type,
    string? Permission,
    string? FeatureCode,
    int DefaultWidth,
    int DefaultHeight,
    string Realm = Realms.Tenant);

public sealed record FilePurposeDefinition(
    string Code,
    string ModuleCode,
    IReadOnlyList<string> AllowedExtensions,
    long MaxBytes,
    string UploadPermission,
    string DownloadPermission,
    string DeletePermission,
    string Classification = "internal");

/// <summary>Code-defined manifest of a tenant module. Permissions/menus/widgets in C# are the single source of truth.</summary>
public interface IModuleDefinition
{
    string Code { get; }
    string Name { get; }
    ModuleKind Kind { get; }
    string? ParentCode { get; }
    IReadOnlyList<string> RequiredModules { get; }
    IReadOnlyList<PermissionDefinition> Permissions { get; }
    IReadOnlyList<MenuItemDefinition> Menu { get; }
    IReadOnlyList<FeatureDefinition> Features { get; }
    IReadOnlyList<WidgetDefinition> Widgets { get; }
    IReadOnlyList<FilePurposeDefinition> FilePurposes { get; }
}

public abstract class ModuleDefinitionBase : IModuleDefinition
{
    public abstract string Code { get; }
    public abstract string Name { get; }
    public virtual ModuleKind Kind => ModuleKind.Core;
    public virtual string? ParentCode => null;
    public virtual IReadOnlyList<string> RequiredModules => [];
    public virtual IReadOnlyList<PermissionDefinition> Permissions => [];
    public virtual IReadOnlyList<MenuItemDefinition> Menu => [];
    public virtual IReadOnlyList<FeatureDefinition> Features => [];
    public virtual IReadOnlyList<WidgetDefinition> Widgets => [];
    public virtual IReadOnlyList<FilePurposeDefinition> FilePurposes => [];

    protected PermissionDefinition P(string code, string action, string description, bool scoped = false, bool sensitive = false,
        PermissionKind kind = PermissionKind.Action) =>
        new(code, Code, kind, action, description, scoped, sensitive);
}

public interface IModuleCatalog
{
    IReadOnlyList<IModuleDefinition> Modules { get; }
    IModuleDefinition? Find(string moduleCode);
    PermissionDefinition? FindPermission(string code);
    IEnumerable<PermissionDefinition> AllPermissions { get; }
    IEnumerable<MenuItemDefinition> AllMenuItems { get; }
    IEnumerable<WidgetDefinition> AllWidgets { get; }
    FilePurposeDefinition? FindFilePurpose(string code);
}

public sealed class ModuleCatalog : IModuleCatalog
{
    private readonly Dictionary<string, IModuleDefinition> _modules;
    private readonly Dictionary<string, PermissionDefinition> _permissions;

    public ModuleCatalog(IEnumerable<IModuleDefinition> modules)
    {
        Modules = modules.OrderBy(m => m.Code, StringComparer.Ordinal).ToList();
        _modules = Modules.ToDictionary(m => m.Code, StringComparer.Ordinal);
        _permissions = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);
        foreach (var p in Modules.SelectMany(m => m.Permissions))
        {
            if (!_permissions.TryAdd(p.Code, p))
            {
                throw new InvalidOperationException($"Duplicate permission code '{p.Code}'.");
            }
        }

        Validate();
    }

    public IReadOnlyList<IModuleDefinition> Modules { get; }
    public IModuleDefinition? Find(string moduleCode) => _modules.GetValueOrDefault(moduleCode);
    public PermissionDefinition? FindPermission(string code) => _permissions.GetValueOrDefault(code);
    public IEnumerable<PermissionDefinition> AllPermissions => _permissions.Values;
    public IEnumerable<MenuItemDefinition> AllMenuItems => Modules.SelectMany(m => m.Menu);
    public IEnumerable<WidgetDefinition> AllWidgets => Modules.SelectMany(m => m.Widgets);

    public FilePurposeDefinition? FindFilePurpose(string code) =>
        Modules.SelectMany(m => m.FilePurposes).FirstOrDefault(f => f.Code == code);

    private void Validate()
    {
        foreach (var module in Modules)
        {
            foreach (var required in module.RequiredModules)
            {
                var isCore = required.StartsWith("core.", StringComparison.Ordinal);
                var isParent = module.ParentCode == required;
                if (!isCore && !isParent)
                {
                    throw new InvalidOperationException(
                        $"Module '{module.Code}' declares a hard dependency on '{required}'. Hard dependencies may only target core.* or the parent module.");
                }
            }

            foreach (var p in module.Permissions)
            {
                if (!p.Code.Contains('.', StringComparison.Ordinal) || !PermissionActions.Standard.Contains(p.Action))
                {
                    throw new InvalidOperationException($"Invalid permission definition '{p.Code}'.");
                }
            }
        }
    }
}
