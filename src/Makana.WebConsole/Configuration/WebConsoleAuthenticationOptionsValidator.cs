using Microsoft.Extensions.Options;

namespace Makana.WebConsole.Configuration;

internal sealed class WebConsoleAuthenticationOptionsValidator : IValidateOptions<WebConsoleAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, WebConsoleAuthenticationOptions options)
    {
        List<string> failures = [];
        HashSet<string> providerIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in options.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Key))
            {
                failures.Add("Makana:Authentication:Providers keys must not be blank.");
                continue;
            }

            if (!providerIds.Add(provider.Key))
            {
                failures.Add($"Makana:Authentication:Providers contains a duplicate id '{provider.Key}'.");
            }
        }

        var validateOrganizationProviders = options.Providers.Count > 0;

        foreach (var organization in options.Organizations)
        {
            if (string.IsNullOrWhiteSpace(organization.Key))
            {
                failures.Add("Makana:Authentication:Organizations keys must not be blank.");
                continue;
            }

            if (!validateOrganizationProviders ||
                organization.Value.AllowGlobalProviders ||
                organization.Value.Providers.Count == 0)
            {
                continue;
            }

            foreach (var providerId in organization.Value.Providers)
            {
                if (!providerIds.Contains(providerId))
                {
                    failures.Add($"Makana:Authentication:Organizations:{organization.Key} references unknown provider '{providerId}'.");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
