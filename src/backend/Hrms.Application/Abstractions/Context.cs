using Hrms.Domain.Platform;
using Hrms.Shared.Auditing;

namespace Hrms.Application.Abstractions;

/// <summary>Trusted tenant context for the current request/job. Never built from client-supplied identifiers.</summary>
public sealed record TenantContext(Guid TenantId, int TenantNumber, string Code, string Name, TenantStatus Status, long EntitlementVersion);

public interface ITenantContextAccessor
{
    TenantContext? Current { get; }
    TenantContext Required { get; }
    void Set(TenantContext context);
}

public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private TenantContext? _current;

    public TenantContext? Current => _current;

    public TenantContext Required =>
        _current ?? throw new InvalidOperationException("No tenant context has been established for this scope.");

    public void Set(TenantContext context)
    {
        if (_current is not null && _current.TenantId != context.TenantId)
        {
            throw new InvalidOperationException("Tenant context is immutable within a scope.");
        }

        _current = context;
    }
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Realm { get; }
    Guid? SessionId { get; }
    string? DisplayName { get; }
    string? Email { get; }
    string ActorType { get; }
}

public interface IRequestContext
{
    string? IpAddress { get; }
    string? UserAgent { get; }
    string CorrelationId { get; }
    string Source { get; }
}

public interface IAuditContext
{
    string? Reason { get; }
    IDisposable WithReason(string reason);
    IDisposable WithSource(string source);
}

/// <summary>Scoped, explicitly populated execution state (by the HTTP pipeline or the job runner).</summary>
public sealed class ExecutionContextState : ICurrentUser, IRequestContext, IAuditContext
{
    private string? _sourceOverride;

    public bool IsAuthenticated => UserId is not null;
    public Guid? UserId { get; private set; }
    public string? Realm { get; private set; }
    public Guid? SessionId { get; private set; }
    public string? DisplayName { get; private set; }
    public string? Email { get; private set; }
    public string ActorType { get; private set; } = ActorTypes.Anonymous;
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string CorrelationId { get; private set; } = Guid.NewGuid().ToString("N");
    public string Source => _sourceOverride ?? BaseSource;
    public string BaseSource { get; private set; } = AuditSources.System;
    public string? Reason { get; private set; }

    public void SetRequest(string correlationId, string? ip, string? userAgent, string source)
    {
        CorrelationId = correlationId;
        IpAddress = ip;
        UserAgent = userAgent is { Length: > 512 } ? userAgent[..512] : userAgent;
        BaseSource = source;
    }

    public void SetUser(Guid userId, string realm, Guid? sessionId, string? displayName, string? email, string actorType)
    {
        UserId = userId;
        Realm = realm;
        SessionId = sessionId;
        DisplayName = displayName;
        Email = email;
        ActorType = actorType;
    }

    public void SetJob(string correlationId)
    {
        CorrelationId = correlationId;
        BaseSource = AuditSources.BackgroundJob;
        ActorType = ActorTypes.Job;
        DisplayName = "background-job";
    }

    public IDisposable WithReason(string reason)
    {
        var previous = Reason;
        Reason = reason;
        return new Restore(() => Reason = previous);
    }

    public IDisposable WithSource(string source)
    {
        var previous = _sourceOverride;
        _sourceOverride = source;
        return new Restore(() => _sourceOverride = previous);
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
