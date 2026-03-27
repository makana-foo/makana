using Makana.Api.Storage;
using Microsoft.Extensions.Options;

namespace Makana.Api.Configuration;

internal sealed class EphemeralStateCleanupPolicyValidator : IValidateOptions<EphemeralStateCleanupPolicy>
{
    public ValidateOptionsResult Validate(string? name, EphemeralStateCleanupPolicy options)
    {
        List<string> failures = [];

        if (options.InitialUpdateLeaseDuration <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(EphemeralStateCleanupPolicy.InitialUpdateLeaseDuration)} must be greater than zero.");
        }

        if (options.ExpiredUpdateGracePeriod < TimeSpan.Zero)
        {
            failures.Add($"{nameof(EphemeralStateCleanupPolicy.ExpiredUpdateGracePeriod)} must be zero or greater.");
        }

        if (options.JanitorInterval <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(EphemeralStateCleanupPolicy.JanitorInterval)} must be greater than zero.");
        }

        if (options.CleanupBatchSize <= 0)
        {
            failures.Add($"{nameof(EphemeralStateCleanupPolicy.CleanupBatchSize)} must be greater than zero.");
        }

        if (options.ExpiredApiTokenRetention < TimeSpan.Zero)
        {
            failures.Add($"{nameof(EphemeralStateCleanupPolicy.ExpiredApiTokenRetention)} must be zero or greater.");
        }

        if (options.ExpiredUpdateTokenRetention < TimeSpan.Zero)
        {
            failures.Add($"{nameof(EphemeralStateCleanupPolicy.ExpiredUpdateTokenRetention)} must be zero or greater.");
        }

        if (options.UploadRetention < TimeSpan.Zero)
        {
            failures.Add($"{nameof(EphemeralStateCleanupPolicy.UploadRetention)} must be zero or greater.");
        }

        if (options.RegistryOperationRetention < TimeSpan.Zero)
        {
            failures.Add($"{nameof(EphemeralStateCleanupPolicy.RegistryOperationRetention)} must be zero or greater.");
        }

        if (options.DeploymentRunRetention < TimeSpan.Zero)
        {
            failures.Add($"{nameof(EphemeralStateCleanupPolicy.DeploymentRunRetention)} must be zero or greater.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
