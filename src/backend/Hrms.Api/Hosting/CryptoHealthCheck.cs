using Hrms.Application.Abstractions;
using Hrms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hrms.Api.Hosting;

/// <summary>
/// Crypto readiness: the platform master key must resolve and at least one stored secret must decrypt.
/// Reports only safe diagnostics (source/fingerprint/counts) — never key material or plaintext.
/// </summary>
public sealed class CryptoHealthCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var info = sp.GetRequiredService<IPlatformMasterKeyProvider>().Key;
            var db = sp.GetRequiredService<PlatformDbContext>();
            var secrets = sp.GetRequiredService<ISecretStore>();

            var refs = await db.Secrets.OrderBy(s => s.Ref).Select(s => s.Ref).Take(8).ToListAsync(cancellationToken);
            var decrypted = 0;
            foreach (var secretRef in refs)
            {
                if (await secrets.GetAsync(secretRef, cancellationToken) is not null)
                {
                    decrypted++;
                }
            }

            if (decrypted == 0 && refs.Count > 0)
            {
                return HealthCheckResult.Unhealthy(
                    "master-key/secret decryption failure: no stored secret decrypts under the configured key.",
                    data: new Dictionary<string, object> { ["keySource"] = info.Source, ["keyFingerprint"] = info.Fingerprint });
            }

            return HealthCheckResult.Healthy(
                refs.Count == 0 ? "master key loaded; no secrets stored" : $"master key loaded; {decrypted}/{refs.Count} secret(s) decrypt",
                data: new Dictionary<string, object> { ["keySource"] = info.Source, ["keyFingerprint"] = info.Fingerprint });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"master key not loaded: {ex.Message}");
        }
    }
}
