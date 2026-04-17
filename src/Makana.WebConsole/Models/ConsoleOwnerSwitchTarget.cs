using Microsoft.AspNetCore.Routing;

namespace Makana.WebConsole.Models;

public sealed record ConsoleOwnerSwitchTarget(string Page, RouteValueDictionary RouteValues)
{
    public static ConsoleOwnerSwitchTarget ForSelectedOwner(RouteValueDictionary routeValues)
        => new("/Stacks/Index", routeValues);
}

public static class ConsoleOwnerSwitchTargetResolver
{
    private static readonly HashSet<string> PreservedOwnerScopedPages = new(StringComparer.Ordinal)
    {
        "/Index",
        "/Stacks/Index",
        "/Resources/Index",
        "/Environments/Index",
        "/Deployments/Index",
        "/AiTasks/Index",
        "/Platform/Index",
        "/Settings/Index",
        "/Organizations/Create"
    };

    public static ConsoleOwnerSwitchTarget Resolve(string owner, string? currentPage, RouteValueDictionary? currentRouteValues)
    {
        if (!string.IsNullOrWhiteSpace(currentPage) &&
            currentRouteValues is not null &&
            currentRouteValues.ContainsKey("owner") &&
            PreservedOwnerScopedPages.Contains(currentPage))
        {
            var preservedValues = new RouteValueDictionary(currentRouteValues)
            {
                ["owner"] = owner
            };
            preservedValues.Remove("handler");
            return new ConsoleOwnerSwitchTarget(currentPage, preservedValues);
        }

        return new ConsoleOwnerSwitchTarget("/Index", new RouteValueDictionary(new { owner }));
    }
}
