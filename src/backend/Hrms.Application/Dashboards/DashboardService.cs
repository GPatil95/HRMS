using Hrms.Application.Abstractions;
using Hrms.Application.Authorization;
using Hrms.Application.Modules;
using Hrms.Domain.Tenant;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Dashboards;

public sealed record DashboardWidgetDto(Guid Id, string WidgetCode, string Title, string Type, int X, int Y, int Width, int Height);

public sealed record DashboardDto(Guid Id, string Code, string Name, IReadOnlyList<DashboardWidgetDto> Widgets);

/// <summary>Supplies data for one widget code. Business modules register providers for their own widgets.</summary>
public interface IWidgetDataProvider
{
    string WidgetCode { get; }
    Task<object> GetDataAsync(CancellationToken ct);
}

/// <summary>
/// Dashboard framework: resolves the user's dashboard (user > role > tenant default) and filters widgets by
/// module entitlement, feature entitlement and permission. Widget data is served through the same checks.
/// </summary>
public sealed class DashboardService(
    ITenantDbContext db,
    ITenantContextAccessor tenant,
    ICurrentUser user,
    IModuleCatalog catalog,
    IEntitlementService entitlements,
    IPermissionService permissions,
    IEnumerable<IWidgetDataProvider> providers)
{
    public async Task<DashboardDto?> GetMineAsync(CancellationToken ct)
    {
        var uid = user.UserId!.Value;
        var roleIds = await db.RoleAssignments.AsNoTracking().Where(a => a.UserId == uid).Select(a => a.RoleId).ToListAsync(ct);
        var assignments = await db.DashboardAssignments.AsNoTracking().ToListAsync(ct);
        var chosen = assignments.FirstOrDefault(a => a.AssigneeType == DashboardAssigneeType.User && a.AssigneeId == uid)
                     ?? assignments.FirstOrDefault(a => a.AssigneeType == DashboardAssigneeType.Role && a.AssigneeId is { } r && roleIds.Contains(r))
                     ?? assignments.FirstOrDefault(a => a.AssigneeType == DashboardAssigneeType.TenantDefault);
        if (chosen is null)
        {
            return null;
        }

        var dashboard = await db.Dashboards.AsNoTracking().Include(d => d.Widgets).FirstOrDefaultAsync(d => d.Id == chosen.DashboardId, ct);
        if (dashboard is null)
        {
            return null;
        }

        var widgets = new List<DashboardWidgetDto>();
        foreach (var w in dashboard.Widgets.OrderBy(w => w.Y).ThenBy(w => w.X))
        {
            var def = catalog.AllWidgets.FirstOrDefault(d => d.Code == w.WidgetCode);
            if (def is not null && await CanSeeAsync(def, ct))
            {
                widgets.Add(new DashboardWidgetDto(w.Id, w.WidgetCode, def.Title, def.Type, w.X, w.Y, w.Width, w.Height));
            }
        }

        return new DashboardDto(dashboard.Id, dashboard.Code, dashboard.Name, widgets);
    }

    public async Task<(bool Found, object? Data)> GetWidgetDataAsync(string widgetCode, CancellationToken ct)
    {
        var def = catalog.AllWidgets.FirstOrDefault(d => d.Code == widgetCode);
        var provider = providers.FirstOrDefault(p => p.WidgetCode == widgetCode);
        if (def is null || provider is null || !await CanSeeAsync(def, ct))
        {
            return (false, null);
        }

        return (true, await provider.GetDataAsync(ct));
    }

    private async Task<bool> CanSeeAsync(WidgetDefinition def, CancellationToken ct)
    {
        var ent = await entitlements.GetAsync(tenant.Required.TenantId, ct);
        if (!ent.IsModuleEnabled(def.ModuleCode) || (def.FeatureCode is { } f && !ent.IsFeatureEnabled(f)))
        {
            return false;
        }

        return def.Permission is null || await permissions.HasAsync(user.UserId!.Value, def.Permission, ct);
    }
}

public sealed class WelcomeWidgetProvider(ICurrentUser user, ITenantContextAccessor tenant) : IWidgetDataProvider
{
    public string WidgetCode => "core.welcome";
    public Task<object> GetDataAsync(CancellationToken ct) => Task.FromResult<object>(new { displayName = user.DisplayName, tenantName = tenant.Required.Name });
}

public sealed class NotificationsWidgetProvider(ITenantDbContext db, ICurrentUser user) : IWidgetDataProvider
{
    public string WidgetCode => "core.notifications";

    public async Task<object> GetDataAsync(CancellationToken ct)
    {
        var uid = user.UserId!.Value;
        var unread = await db.Notifications.CountAsync(n => n.UserId == uid && !n.IsRead, ct);
        var latest = await db.Notifications.AsNoTracking().Where(n => n.UserId == uid).OrderByDescending(n => n.CreatedAt).Take(5)
            .Select(n => new { n.Id, n.Title, n.IsRead, n.CreatedAt }).ToListAsync(ct);
        return new { unread, latest };
    }
}
