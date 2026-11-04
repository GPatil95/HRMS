using Hrms.Application.Abstractions;
using Hrms.Application.Jobs;
using Hrms.Domain.Common;
using Hrms.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hrms.Application.Notifications;

/// <summary>Queues outgoing email in the realm database (transactional outbox) and schedules dispatch.</summary>
public interface IEmailOutbox
{
    Task QueueAsync(OutgoingEmail email, string? templateCode, CancellationToken ct);
}

public sealed class TenantEmailOutbox(ITenantDbContext db, ITenantContextAccessor tenant, IJobScheduler jobs, IRequestContext request, IClock clock) : IEmailOutbox
{
    public async Task QueueAsync(OutgoingEmail email, string? templateCode, CancellationToken ct)
    {
        db.EmailMessages.Add(EmailOutboxMapper.Map(email, templateCode, request.CorrelationId, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        jobs.EnqueueTenant(tenant.Required.TenantId, JobTypes.TenantEmailDispatch);
    }
}

public sealed class PlatformEmailOutbox(IPlatformDbContext db, IJobScheduler jobs, IRequestContext request, IClock clock) : IEmailOutbox
{
    public async Task QueueAsync(OutgoingEmail email, string? templateCode, CancellationToken ct)
    {
        db.EmailMessages.Add(EmailOutboxMapper.Map(email, templateCode, request.CorrelationId, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        jobs.EnqueuePlatform(JobTypes.PlatformEmailDispatch);
    }
}

internal static class EmailOutboxMapper
{
    public static EmailMessage Map(OutgoingEmail e, string? templateCode, string correlationId, DateTimeOffset now) => new()
    {
        CreatedAt = now,
        To = [.. e.To],
        Cc = [.. e.Cc],
        Subject = e.Subject,
        HtmlBody = e.HtmlBody,
        TextBody = e.TextBody,
        TemplateCode = templateCode,
        Status = EmailStatus.Queued,
        CorrelationId = correlationId,
    };
}

/// <summary>Sends pending outbox messages with bounded retries. Used by both realm dispatch jobs.</summary>
public static class EmailDispatcher
{
    public const int MaxAttempts = 5;

    public static async Task<int> DispatchAsync(DbSet<EmailMessage> set, Func<CancellationToken, Task<int>> save, IEmailTransport transport, IClock clock, ILogger logger, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var pending = await set.Where(m => (m.Status == EmailStatus.Queued || m.Status == EmailStatus.Retrying) && m.Attempts < MaxAttempts && (m.ScheduledAt == null || m.ScheduledAt <= now))
            .OrderBy(m => m.CreatedAt).Take(50).ToListAsync(ct);
        var sent = 0;
        foreach (var m in pending)
        {
            m.Attempts++;
            try
            {
                m.ProviderMessageId = await transport.SendAsync(new OutgoingEmail(m.To, m.Cc, m.Subject, m.HtmlBody, m.TextBody, []), ct);
                m.Status = EmailStatus.Sent;
                m.SentAt = clock.UtcNow;
                m.LastError = null;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never log recipients or bodies; message id + error type only.
                logger.LogWarning("Email {MessageId} send attempt {Attempt} failed: {Error}", m.Id, m.Attempts, ex.GetType().Name);
                m.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                m.Status = m.Attempts >= MaxAttempts ? EmailStatus.Failed : EmailStatus.Retrying;
                m.ScheduledAt = clock.UtcNow.AddSeconds(30 * Math.Pow(2, m.Attempts));
            }

            await save(ct);
        }

        return sent;
    }
}

[JobHandler(JobTypes.TenantEmailDispatch, true)]
public sealed class TenantEmailDispatchJob(ITenantDbContext db, IEmailTransport transport, IClock clock, ILogger<TenantEmailDispatchJob> logger) : IJobHandler
{
    public string JobType => JobTypes.TenantEmailDispatch;
    public bool TenantScoped => true;
    public Task ExecuteAsync(string? payload, CancellationToken ct) => EmailDispatcher.DispatchAsync(db.EmailMessages, db.SaveChangesAsync, transport, clock, logger, ct);
}

[JobHandler(JobTypes.PlatformEmailDispatch, false)]
public sealed class PlatformEmailDispatchJob(IPlatformDbContext db, IEmailTransport transport, IClock clock, ILogger<PlatformEmailDispatchJob> logger) : IJobHandler
{
    public string JobType => JobTypes.PlatformEmailDispatch;
    public bool TenantScoped => false;
    public Task ExecuteAsync(string? payload, CancellationToken ct) => EmailDispatcher.DispatchAsync(db.EmailMessages, db.SaveChangesAsync, transport, clock, logger, ct);
}

/// <summary>Renders tenant-managed templates (Scriban) for authentication emails. Links use the tenant's own host.</summary>
public sealed class TenantAuthEmailSender(
    ITenantDbContext db,
    ITenantContextAccessor tenant,
    ITemplateRenderer renderer,
    IEmailOutbox outbox,
    Microsoft.Extensions.Options.IOptions<HostingOptions> hosting,
    Microsoft.Extensions.Options.IOptions<IdentityOptions> identity) : Identity.IAuthEmailSender
{
    public Task SendInvitationAsync(IdentityUserBase user, string token, CancellationToken ct) =>
        SendAsync(Tenancy.SystemTemplates.Invitation, user, $"/accept-invite?token={Uri.EscapeDataString(token)}", identity.Value.InvitationTokenHours, ct);

    public Task SendPasswordResetAsync(IdentityUserBase user, string token, CancellationToken ct) =>
        SendAsync(Tenancy.SystemTemplates.PasswordReset, user, $"/reset-password?token={Uri.EscapeDataString(token)}", Math.Max(1, identity.Value.PasswordResetTokenMinutes / 60), ct);

    public Task SendEmailVerificationAsync(IdentityUserBase user, string token, CancellationToken ct) =>
        SendAsync(Tenancy.SystemTemplates.EmailVerification, user, $"/verify-email?token={Uri.EscapeDataString(token)}", identity.Value.EmailVerificationTokenHours, ct);

    private async Task SendAsync(string code, IdentityUserBase user, string path, int hours, CancellationToken ct)
    {
        var t = tenant.Required;
        var template = await db.NotificationTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == code && x.Channel == Domain.Tenant.NotificationChannels.Email && x.IsActive, ct)
            ?? throw new InvalidOperationException($"Email template {code} is missing.");
        var model = new Dictionary<string, object?>
        {
            ["tenant_name"] = t.Name,
            ["display_name"] = user.DisplayName,
            ["link"] = hosting.Value.BuildUrl($"{t.Code}.{hosting.Value.BaseDomain}", path),
            ["hours"] = hours,
        };
        await outbox.QueueAsync(new OutgoingEmail([user.Email], [], renderer.Render(template.Subject, model), renderer.Render(template.Body, model), null, []), code, ct);
    }
}

public sealed class PlatformAuthEmailSender(
    ITemplateRenderer renderer,
    IEmailOutbox outbox,
    Microsoft.Extensions.Options.IOptions<HostingOptions> hosting) : Identity.IAuthEmailSender
{
    private const string Body = "<p>Hello {{ display_name }},</p><p>{{ message }}</p><p><a href=\"{{ link }}\">{{ link }}</a></p>";

    public Task SendInvitationAsync(IdentityUserBase user, string token, CancellationToken ct) =>
        SendAsync(user, "Platform administration invitation", "You have been invited to the HRMS platform administration portal.", $"/admin/accept-invite?token={Uri.EscapeDataString(token)}", ct);

    public Task SendPasswordResetAsync(IdentityUserBase user, string token, CancellationToken ct) =>
        SendAsync(user, "Reset your platform password", "Use the link below to reset your password.", $"/admin/reset-password?token={Uri.EscapeDataString(token)}", ct);

    public Task SendEmailVerificationAsync(IdentityUserBase user, string token, CancellationToken ct) =>
        SendAsync(user, "Verify your email", "Use the link below to verify your email.", $"/admin/verify-email?token={Uri.EscapeDataString(token)}", ct);

    private Task SendAsync(IdentityUserBase user, string subject, string message, string path, CancellationToken ct)
    {
        var model = new Dictionary<string, object?>
        {
            ["display_name"] = user.DisplayName,
            ["message"] = message,
            ["link"] = hosting.Value.BuildUrl(hosting.Value.AdminHosts[0], path),
        };
        return outbox.QueueAsync(new OutgoingEmail([user.Email], [], subject, renderer.Render(Body, model), null, []), null, ct);
    }
}
