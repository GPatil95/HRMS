using Hrms.Application.Abstractions;
using Hrms.Application.Identity;
using Hrms.Application.Tenancy;
using Hrms.Domain.Common;
using Hrms.Shared.Auditing;
using Hrms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hrms.Infrastructure.Tenancy;

/// <summary>After activation, sends the tenant admin invitation from inside the tenant's own context and database.</summary>
public sealed class TenantAdminInvitationHandler(IServiceScopeFactory scopes, IRequestContext request) : ITenantActivationHandler
{
    public async Task OnActivatedAsync(Guid tenantId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ExecutionContextState>().SetRequest(request.CorrelationId, null, null, AuditSources.System);
        var registry = sp.GetRequiredService<ITenantRegistry>();
        await registry.InvalidateAsync(tenantId, ct);
        var tenant = await registry.FindByIdAsync(tenantId, ct) ?? throw new InvalidOperationException("Activated tenant not found.");
        sp.GetRequiredService<ITenantContextAccessor>().Set(tenant);
        await sp.GetRequiredService<ITenantDatabaseResolver>().EnsureResolvedAsync(tenant, ct);

        var adminEmail = (await sp.GetRequiredService<IPlatformDbContext>().Tenants.Where(t => t.Id == tenantId).Select(t => t.AdminEmail).FirstAsync(ct)).Trim().ToLowerInvariant();
        var user = await sp.GetRequiredService<ITenantDbContext>().Users.FirstOrDefaultAsync(u => u.Email == adminEmail && u.Status == UserStatus.Invited, ct);
        if (user is not null)
        {
            await sp.GetRequiredKeyedService<AuthService>(Realms.Tenant).SendInvitationAsync(user, ct);
        }
    }
}
