namespace Makana.Api.Storage;

public class EphemeralStateCleanupPolicy
{
    public TimeSpan InitialUpdateLeaseDuration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ExpiredUpdateGracePeriod { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan JanitorInterval { get; init; } = TimeSpan.FromMinutes(1);
    public int CleanupBatchSize { get; init; } = 128;

    public TimeSpan ExpiredApiTokenRetention { get; init; } = TimeSpan.FromDays(1);
    public TimeSpan ExpiredUpdateTokenRetention { get; init; } = TimeSpan.FromDays(1);
    public TimeSpan UploadRetention { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan RegistryOperationRetention { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan DeploymentRunRetention { get; init; } = TimeSpan.FromDays(7);
}
