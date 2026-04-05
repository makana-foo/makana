using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Makana.Authentication;

internal sealed class OidcLifecycleHostedService(
    OidcAuthorityRegistry registry,
    ILogger<OidcLifecycleHostedService> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await registry.WarmupAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "OIDC authority warmup failed during startup.");
            throw;
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await registry.RefreshScheduledAuthoritiesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OIDC authority background refresh failed.");
            }
        }
    }
}
