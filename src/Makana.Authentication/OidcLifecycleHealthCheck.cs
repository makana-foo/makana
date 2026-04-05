using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Makana.Authentication;

internal sealed class OidcLifecycleHealthCheck(
    OidcAuthorityRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        var status = HealthStatus.Healthy;

        foreach (var authority in registry.GetAuthorities())
        {
            data[$"{authority.Name}.metadataAddress"] = authority.Options.MetadataAddress;
            data[$"{authority.Name}.lastSuccessfulRefreshUtc"] = authority.Status.LastSuccessfulRefreshUtc?.ToString("O") ?? string.Empty;
            data[$"{authority.Name}.lastFailureUtc"] = authority.Status.LastFailureUtc?.ToString("O") ?? string.Empty;
            data[$"{authority.Name}.lastFailureMessage"] = authority.Status.LastFailureMessage ?? string.Empty;

            if (authority.Status.LastSuccessfulRefreshUtc is null)
            {
                status = HealthStatus.Unhealthy;
                continue;
            }

            if (authority.Status.LastFailureUtc is not null && status != HealthStatus.Unhealthy)
            {
                status = HealthStatus.Degraded;
            }
        }

        return Task.FromResult(status switch
        {
            HealthStatus.Healthy => HealthCheckResult.Healthy("OIDC authority lifecycle is healthy.", data),
            HealthStatus.Degraded => HealthCheckResult.Degraded("OIDC authority lifecycle is using stale or last-known-good state.", data: data),
            _ => HealthCheckResult.Unhealthy("OIDC authority lifecycle did not complete startup warmup.", data: data)
        });
    }
}
