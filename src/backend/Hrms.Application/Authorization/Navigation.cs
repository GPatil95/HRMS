using Hrms.Application.Abstractions;
using Hrms.Application.Modules;

namespace Hrms.Application.Authorization;

public sealed record NavigationItem(string Code, string Title, string? Route, string? Icon, int Order, string ModuleCode, IReadOnlyList<NavigationItem> Children);

public interface INavigationService
{
    Task<IReadOnlyList<NavigationItem>> GetTenantNavigationAsync(Guid userId, CancellationToken ct);
}

public sealed class NavigationService(
    IModuleCatalog catalog,
    IEntitlementService entitlements,
    IPermissionService permissions,
    ITenantContextAccessor tenant) : INavigationService
{
    public async Task<IReadOnlyList<NavigationItem>> GetTenantNavigationAsync(Guid userId, CancellationToken ct)
    {
        var ent = await entitlements.GetAsync(tenant.Required.TenantId, ct);
        var perms = await permissions.GetEffectiveAsync(userId, ct);
        return Build(catalog.AllMenuItems.ToList(), ent.IsModuleEnabled, ent.IsFeatureEnabled, perms.Has);
    }

    public static IReadOnlyList<NavigationItem> Build(
        IReadOnlyList<MenuItemDefinition> items,
        Func<string, bool> moduleEnabled,
        Func<string, bool> featureEnabled,
        Func<string, bool> hasPermission)
    {
        bool Allowed(MenuItemDefinition m) =>
            m.Visible
            && (m.ModuleCode == "platform" || moduleEnabled(m.ModuleCode))
            && (m.FeatureCode is null || featureEnabled(m.FeatureCode))
            && (m.AnyPermissions is null || m.AnyPermissions.Count == 0 || m.AnyPermissions.Any(hasPermission));

        List<NavigationItem> Children(string? parent) => items
            .Where(m => m.ParentCode == parent && Allowed(m))
            .Select(m => new NavigationItem(m.Code, m.Title, m.Route, m.Icon, m.Order, m.ModuleCode, Children(m.Code)))
            .Where(n => n.Route is not null || n.Children.Count > 0)
            .OrderBy(n => n.Order)
            .ToList();

        return Children(null);
    }
}
