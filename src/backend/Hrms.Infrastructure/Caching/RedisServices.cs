using System.Collections.Concurrent;
using Hrms.Application.Abstractions;
using StackExchange.Redis;

namespace Hrms.Infrastructure.Caching;

public sealed class RedisDistributedLockProvider(IConnectionMultiplexer redis) : IDistributedLockProvider
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        var token = Guid.NewGuid().ToString("N");
        var db = redis.GetDatabase();
        return await db.StringSetAsync($"lock:{key}", token, ttl, When.NotExists) ? new Handle(db, $"lock:{key}", token) : null;
    }

    private sealed class Handle(IDatabase db, string key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await db.ScriptEvaluateAsync("if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end", [key], [token]);
    }
}

public sealed class RedisRateLimiter(IConnectionMultiplexer redis) : IRateLimiter
{
    public async Task<bool> TryAcquireAsync(string key, int limit, TimeSpan window, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var k = $"rl:{key}";
        var count = await db.StringIncrementAsync(k);
        if (count == 1)
        {
            await db.KeyExpireAsync(k, window);
        }

        return count <= limit;
    }
}

public sealed class RedisSessionCache(IConnectionMultiplexer redis) : ISessionCache
{
    private static string Key(string realm, Guid? tenantId, Guid sessionId) => $"sess:{realm}:{tenantId?.ToString("N") ?? "plat"}:{sessionId:N}";

    public async Task<bool?> IsActiveAsync(string realm, Guid? tenantId, Guid sessionId, CancellationToken ct)
    {
        var v = await redis.GetDatabase().StringGetAsync(Key(realm, tenantId, sessionId));
        return v.IsNull ? null : v == "1";
    }

    public Task MarkActiveAsync(string realm, Guid? tenantId, Guid sessionId, TimeSpan ttl, CancellationToken ct) =>
        redis.GetDatabase().StringSetAsync(Key(realm, tenantId, sessionId), "1", ttl);

    public Task MarkRevokedAsync(string realm, Guid? tenantId, Guid sessionId, CancellationToken ct) =>
        redis.GetDatabase().StringSetAsync(Key(realm, tenantId, sessionId), "0", TimeSpan.FromHours(24));
}

public sealed class RedisCacheInvalidationBus(IConnectionMultiplexer redis) : ICacheInvalidationBus
{
    public Task PublishAsync(string channel, string message, CancellationToken ct) =>
        redis.GetSubscriber().PublishAsync(RedisChannel.Literal(channel), message);
}

/// <summary>Single-instance fallbacks used when Redis is not configured (unit tests, tooling).</summary>
public sealed class InMemoryDistributedLockProvider : IDistributedLockProvider
{
    private static readonly ConcurrentDictionary<string, byte> Held = new();

    public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct) =>
        Task.FromResult<IAsyncDisposable?>(Held.TryAdd(key, 0) ? new Handle(key) : null);

    private sealed class Handle(string key) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Held.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class InMemoryRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Start, int Count)> _windows = new();

    public Task<bool> TryAcquireAsync(string key, int limit, TimeSpan window, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var w = _windows.AddOrUpdate(key, _ => (now, 1), (_, v) => now - v.Start > window ? (now, 1) : (v.Start, v.Count + 1));
        return Task.FromResult(w.Count <= limit);
    }
}

public sealed class InMemorySessionCache : ISessionCache
{
    private readonly ConcurrentDictionary<string, bool> _state = new();

    public Task<bool?> IsActiveAsync(string realm, Guid? tenantId, Guid sessionId, CancellationToken ct) =>
        Task.FromResult(_state.TryGetValue($"{realm}:{tenantId}:{sessionId}", out var v) ? v : (bool?)null);

    public Task MarkActiveAsync(string realm, Guid? tenantId, Guid sessionId, TimeSpan ttl, CancellationToken ct)
    {
        _state[$"{realm}:{tenantId}:{sessionId}"] = true;
        return Task.CompletedTask;
    }

    public Task MarkRevokedAsync(string realm, Guid? tenantId, Guid sessionId, CancellationToken ct)
    {
        _state[$"{realm}:{tenantId}:{sessionId}"] = false;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryCacheInvalidationBus : ICacheInvalidationBus
{
    public Task PublishAsync(string channel, string message, CancellationToken ct) => Task.CompletedTask;
}
