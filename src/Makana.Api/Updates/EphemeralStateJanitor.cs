using Makana.Api.Realtime;
using Makana.Api.Storage;
using Microsoft.Extensions.Options;

namespace Makana.Api.Updates;

internal sealed class EphemeralStateJanitor(
    MaintenanceRepository maintenanceRepository,
    IUpdateCheckpointJournalingProvider checkpointJournalingProvider,
    IStackUpdateNotifier updateNotifier,
    IOptions<EphemeralStateCleanupPolicy> cleanupOptions,
    ILogger<EphemeralStateJanitor> logger) : IMakanaApiMaintenanceRunner
{
    public EphemeralStateCleanupPolicy EphemeralStateCleanupPolicy => cleanupOptions.Value;

    public Task CancelExpiredUpdatesForStackAsync(string stackId, CancellationToken cancellationToken)
        => CancelExpiredUpdates(stackId);

    public async Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        await CancelExpiredUpdates(stackId: null);
        await PruneTransientStateAsync(cancellationToken);
    }

    private async Task CancelExpiredUpdates(string? stackId)
    {
        var now = DateTimeOffset.UtcNow;
        var graceCutoff = now - EphemeralStateCleanupPolicy.ExpiredUpdateGracePeriod;
        var expiredUpdates = maintenanceRepository.GetExpiredUpdateCandidates(
            stackId,
            now,
            graceCutoff,
            EphemeralStateCleanupPolicy.CleanupBatchSize);

        foreach (var expiredUpdate in expiredUpdates)
        {
            var cancellationResult = maintenanceRepository.CancelExpiredUpdateAndDeleteTokens(
                expiredUpdate.UpdateId,
                now,
                graceCutoff);
            if (!cancellationResult.Canceled)
            {
                continue;
            }

            await checkpointJournalingProvider.RecoverInterruptedUpdateAsync(expiredUpdate.UpdateId);
            var message = new StackUpdateChangedMessage(
                expiredUpdate.Owner,
                expiredUpdate.Project,
                expiredUpdate.Stack,
                expiredUpdate.StackId,
                expiredUpdate.UpdateId,
                cancellationResult.LatestEventSequence,
                "cancelled",
                now);
            await updateNotifier.NotifyChangedAsync(message);

            logger.LogInformation(
                "Canceled expired update lease for {Owner}/{Project}/{Stack} update {UpdateId}",
                expiredUpdate.Owner,
                expiredUpdate.Project,
                expiredUpdate.Stack,
                expiredUpdate.UpdateId);
        }
    }

    private async Task PruneTransientStateAsync(CancellationToken cancellationToken)
    {
        var (apiTokens, updateTokens, uploads, registryOperations, deploymentRuns)
            = maintenanceRepository.PruneTransientState();

        LogDeletion("api tokens", apiTokens);
        LogDeletion("update tokens", updateTokens);
        LogDeletion("uploads", uploads);
        LogDeletion("registry operations", registryOperations);
        LogDeletion("deployment runs", deploymentRuns);

        void LogDeletion(string category, int deleted)
        {
            if (deleted is not > 0)
            {
                return;
            }

            if (logger?.IsEnabled(LogLevel.Information) is not true)
            {
                return;
            }

            logger.LogInformation("Deleted {DeletedCount} expired {Category}", deleted, category);
        }
    }
}

internal sealed class EphemeralStateJanitorHostedService(
    IMakanaApiMaintenanceRunner janitor,
    MakanaApiStorageRuntime storageRuntime,
    IOptions<EphemeralStateCleanupPolicy> cleanupOptions) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (storageRuntime.CanRunMaintenance)
        {
            await janitor.RunMaintenanceAsync(cancellationToken);
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        var cleanupPolicy = cleanupOptions.Value;

        using var timer = new PeriodicTimer(cleanupPolicy.JanitorInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!storageRuntime.CanRunMaintenance)
            {
                continue;
            }

            await janitor.RunMaintenanceAsync(stoppingToken);
        }
    }
}
