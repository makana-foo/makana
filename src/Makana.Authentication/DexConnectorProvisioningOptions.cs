using Microsoft.Extensions.Options;

namespace Makana.Authentication;

public sealed class DexConnectorProvisioningOptions
{
    public const string SectionName = "Dex";

    public string GrpcAddress { get; set; } = string.Empty;
}

internal sealed class DexConnectorProvisioningOptionsValidator : IValidateOptions<DexConnectorProvisioningOptions>
{
    public ValidateOptionsResult Validate(string? name, DexConnectorProvisioningOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.GrpcAddress))
        {
            return ValidateOptionsResult.Success;
        }

        return Uri.TryCreate(options.GrpcAddress, UriKind.Absolute, out var address) &&
               (string.Equals(address.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(address.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Dex:GrpcAddress must be an absolute http or https URL when configured.");
    }
}
