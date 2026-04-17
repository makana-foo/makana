using Makana.WebConsole.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;

namespace Makana.WebConsole.Services;

public sealed class ConsoleContextService(PulumiApiClient apiClient)
{
    private const string ContextItemKey = "Makana.WebConsole.NavigationContext";

    public async Task<ConsoleNavigationContext> GetContextAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (httpContext.Items.TryGetValue(ContextItemKey, out var cached)
            && cached is ConsoleNavigationContext cachedContext)
        {
            return cachedContext;
        }

        var user = await apiClient.GetUserAsync(cancellationToken);
        var stacks = await apiClient.GetUserStacksAsync(cancellationToken);
        var signedInOwners = await GetSignedInOwnersAsync(httpContext);

        var personalOwner = ResolvePersonalOwner(user);
        var ownerOptions = await BuildOwnerOptionsAsync(user, personalOwner, signedInOwners, cancellationToken);
        var ownerGroups = BuildOwnerGroups(ownerOptions);
        var requestedOwner = httpContext.GetRouteValue("owner")?.ToString();
        var selectedOwner = ResolveSelectedOwner(requestedOwner, ownerOptions, personalOwner);
        var requestedOwnerIsAuthorized = string.IsNullOrWhiteSpace(requestedOwner) ||
                                         ownerOptions.Any(option => string.Equals(option.Value, requestedOwner, StringComparison.Ordinal));
        var selectedOwnerIsAuthorized = ownerOptions.Any(option => string.Equals(option.Value, selectedOwner, StringComparison.Ordinal));
        var selectedStacks = selectedOwnerIsAuthorized
            ? stacks
                .Where(stack => string.Equals(stack.OrgName, selectedOwner, StringComparison.Ordinal))
                .OrderBy(stack => stack.ProjectName, StringComparer.Ordinal)
                .ThenBy(stack => stack.StackName, StringComparer.Ordinal)
                .ToArray()
            : [];

        var context = new ConsoleNavigationContext(
            user,
            selectedOwner,
            requestedOwner,
            requestedOwnerIsAuthorized,
            ownerOptions,
            ownerGroups,
            stacks,
            selectedStacks);

        httpContext.Items[ContextItemKey] = context;
        return context;
    }

    private static string ResolvePersonalOwner(ServiceUser? user)
    {
        if (!string.IsNullOrWhiteSpace(user?.GitHubLogin))
        {
            return user.GitHubLogin;
        }

        if (!string.IsNullOrWhiteSpace(user?.Name))
        {
            return user.Name;
        }

        if (!string.IsNullOrWhiteSpace(user?.Id))
        {
            return user.Id;
        }

        return "personal";
    }

    private async Task<ConsoleOwnerOption[]> BuildOwnerOptionsAsync(
        ServiceUser? user,
        string personalOwner,
        IReadOnlySet<string> signedInOwners,
        CancellationToken cancellationToken)
    {
        var organizationEntries = new List<ConsoleOwnerOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (user?.Organizations is not null)
        {
            foreach (var organization in user.Organizations)
            {
                var orgName = !string.IsNullOrWhiteSpace(organization.GitHubLogin)
                    ? organization.GitHubLogin
                    : organization.Name;
                if (string.IsNullOrWhiteSpace(orgName) ||
                    string.Equals(orgName, personalOwner, StringComparison.OrdinalIgnoreCase) ||
                    !seen.Add(orgName))
                {
                    continue;
                }

                var requiresOrganizationSignIn = await RequiresOrganizationSignInAsync(orgName, cancellationToken);
                organizationEntries.Add(new ConsoleOwnerOption(
                    orgName,
                    orgName,
                    false,
                    signedInOwners.Contains(orgName),
                    requiresOrganizationSignIn));
            }
        }

        organizationEntries.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Label, right.Label));

        var entries = new List<ConsoleOwnerOption>(organizationEntries.Count + 1);
        entries.AddRange(organizationEntries);
        entries.Add(new ConsoleOwnerOption(personalOwner, personalOwner, true, true, false));

        return [.. entries];
    }

    private static ConsoleOwnerGroup[] BuildOwnerGroups(IReadOnlyList<ConsoleOwnerOption> ownerOptions)
    {
        var groups = new List<ConsoleOwnerGroup>(2);

        var organizations = ownerOptions
            .Where(option => !option.IsPersonal)
            .ToArray();
        if (organizations.Length > 0)
        {
            groups.Add(new ConsoleOwnerGroup("Organizations", organizations));
        }

        var personal = ownerOptions
            .Where(option => option.IsPersonal)
            .ToArray();
        if (personal.Length > 0)
        {
            groups.Add(new ConsoleOwnerGroup("Individual", personal));
        }

        return [.. groups];
    }

    private static string ResolveSelectedOwner(string? requestedOwner, IReadOnlyList<ConsoleOwnerOption> ownerOptions, string personalOwner)
    {
        if (!string.IsNullOrWhiteSpace(requestedOwner)
            && ownerOptions.Any(option => string.Equals(option.Value, requestedOwner, StringComparison.Ordinal)))
        {
            return requestedOwner;
        }

        if (!string.IsNullOrWhiteSpace(requestedOwner))
        {
            return requestedOwner;
        }

        var personalOption = ownerOptions.FirstOrDefault(option => option.IsPersonal);
        if (personalOption is not null)
        {
            return personalOption.Value;
        }

        return ownerOptions.FirstOrDefault()?.Value ?? personalOwner;
    }

    private static async Task<IReadOnlySet<string>> GetSignedInOwnersAsync(HttpContext httpContext)
    {
        var requestServices = httpContext.RequestServices;
        if (requestServices is null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var authService = requestServices.GetService<IAuthenticationService>();
        if (authService is null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var authResult = await httpContext.AuthenticateAsync();
        return WebConsoleAuthenticationState.GetSignedInOwners(authResult.Properties);
    }

    private async Task<bool> RequiresOrganizationSignInAsync(string organization, CancellationToken cancellationToken)
    {
        var organizationLogin = await apiClient.GetOrganizationLoginAsync(organization, cancellationToken);
        if (organizationLogin is null)
        {
            return false;
        }

        return organizationLogin is not null;
    }
}
