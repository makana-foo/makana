using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Makana.Api.Storage;

public sealed class MakanaApiStorageHealthCheck(MakanaApiStorageRuntime storageRuntime) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(storageRuntime.ToHealthCheckResult());
    }
}
