using Hrms.Application.Abstractions;
using Hrms.Domain.Tenant;
using Hrms.Shared.Paging;
using Hrms.Shared.Primitives;
using Hrms.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Application.Notifications;

public sealed record NotificationDto(Guid Id, string Type, string Title, string? Body, string? Link, NotificationPriority Priority, bool IsRead, DateTimeOffset CreatedAt);

public sealed record NotifyCommand(Guid UserId, string Type, string Title, string? Body, string? Link, NotificationPriority Priority = NotificationPriority.Normal, bool AlsoEmail = false);

/// <summary>Notification foundation: in-app inbox, realtime push and optional email, honouring user channel preferences.</summary>
public interface INotificationService
{
    Task NotifyAsync(NotifyCommand cmd, CancellationToken ct);
}

public sealed class NotificationService(
    ITenantDbContext db,
    ITenantContextAccessor tenant,
    ICurrentUser user,
    IRealtimeNotifier realtime,
    IEmailOutbox email,
    IClock clock) : INotificationService
{
    public async Task NotifyAsync(NotifyCommand cmd, CancellationToken ct)
    {
        var recipient = await db.Users.AsNoTracking().Where(u => u.Id == cmd.UserId).Select(u => new { u.Email, u.Status }).FirstOrDefaultAsync(ct);
        if (recipient is null)
        {
            return;
        }

        var prefs = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == cmd.UserId && p.NotificationType == cmd.Type).ToListAsync(ct);
        bool Enabled(string channel, bool @default) => prefs.FirstOrDefault(p => p.Channel == channel)?.Enabled ?? @default;

        if (Enabled(NotificationChannels.InApp, true) || cmd.Priority == NotificationPriority.Critical)
        {
            var n = new Notification { UserId = cmd.UserId, Type = cmd.Type, Title = cmd.Title, Body = cmd.Body, Link = cmd.Link, Priority = cmd.Priority, CreatedAt = clock.UtcNow };
            db.Notifications.Add(n);
            await db.SaveChangesAsync(ct);
            await realtime.NotifyUserAsync(tenant.Required.TenantId, cmd.UserId, "notification", new { n.Id, n.Title, n.Type }, ct);
        }

        if (cmd.AlsoEmail && Enabled(NotificationChannels.Email, true))
        {
            await email.QueueAsync(new Abstractions.OutgoingEmail([recipient.Email], [], cmd.Title, cmd.Body, cmd.Body, []), cmd.Type, ct);
        }
    }

    public async Task<PagedResult<NotificationDto>> ListMineAsync(PageRequest page, bool unreadOnly, CancellationToken ct)
    {
        var uid = user.UserId!.Value;
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == uid && (!unreadOnly || !n.IsRead));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(n => n.CreatedAt).Skip(page.Skip).Take(page.SafePageSize)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.Priority, n.IsRead, n.CreatedAt)).ToListAsync(ct);
        return new PagedResult<NotificationDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public Task<int> UnreadCountAsync(CancellationToken ct)
    {
        var uid = user.UserId!.Value;
        return db.Notifications.CountAsync(n => n.UserId == uid && !n.IsRead, ct);
    }

    public async Task<Result> MarkReadAsync(Guid id, CancellationToken ct)
    {
        var uid = user.UserId!.Value;
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct);
        if (n is null)
        {
            return Error.NotFound("notification-not-found", "Notification not found.");
        }

        n.IsRead = true;
        n.ReadAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task MarkAllReadAsync(CancellationToken ct)
    {
        var uid = user.UserId!.Value;
        var now = clock.UtcNow;
        await db.Notifications.Where(n => n.UserId == uid && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, now), ct);
    }
}
