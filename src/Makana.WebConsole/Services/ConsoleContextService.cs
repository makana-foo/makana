using Makana.WebConsole.Models;
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

        var personalOwner = ResolvePersonalOwner(user);
        var ownerOptions = BuildOwnerOptions(user, stacks, personalOwner);
        var selectedOwner = ResolveSelectedOwner(httpContext, ownerOptions, personalOwner);
        var selectedStacks = stacks
            .Where(stack => string.Equals(stack.OrgName, selectedOwner, StringComparison.Ordinal))
            .OrderBy(stack => stack.ProjectName, StringComparer.Ordinal)
            .ThenBy(stack => stack.StackName, StringComparer.Ordinal)
            .ToArray();

        var context = new ConsoleNavigationContext(user, selectedOwner, ownerOptions, stacks, selectedStacks);

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

    private static ConsoleOwnerOption[] BuildOwnerOptions(ServiceUser? user, IReadOnlyList<StackSummary> stacks, string personalOwner)
    {
        var entries = new List<ConsoleOwnerOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (seen.Add(personalOwner))
        {
            entries.Add(new ConsoleOwnerOption(personalOwner, $"{personalOwner} (personal)", true));
        }

        if (user?.Organizations is not null)
        {
            foreach (var organization in user.Organizations)
            {
                var orgName = !string.IsNullOrWhiteSpace(organization.GitHubLogin)
                    ? organization.GitHubLogin
                    : organization.Name;
                if (string.IsNullOrWhiteSpace(orgName) || !seen.Add(orgName))
                {
                    continue;
                }

                entries.Add(new ConsoleOwnerOption(orgName, orgName, false));
            }
        }

        foreach (var stackOrg in stacks
                     .Select(stack => stack.OrgName)
                     .Where(org => !string.IsNullOrWhiteSpace(org))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(org => org, StringComparer.Ordinal))
        {
            if (!seen.Add(stackOrg))
            {
                continue;
            }

            entries.Add(new ConsoleOwnerOption(stackOrg, stackOrg, false));
        }

        return entries
            .OrderBy(option => option.IsPersonal ? 0 : 1)
            .ThenBy(option => option.Label, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveSelectedOwner(HttpContext httpContext, IReadOnlyList<ConsoleOwnerOption> ownerOptions, string personalOwner)
    {
        var requestedOwner = httpContext.GetRouteValue("owner")?.ToString();
        if (!string.IsNullOrWhiteSpace(requestedOwner)
            && ownerOptions.Any(option => string.Equals(option.Value, requestedOwner, StringComparison.Ordinal)))
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
}
