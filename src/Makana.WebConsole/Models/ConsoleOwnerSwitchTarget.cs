using Microsoft.AspNetCore.Routing;

namespace Makana.WebConsole.Models;

public sealed record ConsoleOwnerSwitchTarget(string Page, RouteValueDictionary RouteValues)
{
    public static ConsoleOwnerSwitchTarget ForSelectedOwner(RouteValueDictionary routeValues)
        => new("/Stacks/Index", routeValues);
}

public static class ConsoleOwnerSwitchTargetResolver
{
    public static ConsoleOwnerSwitchTarget Resolve(string owner, string? currentPage, RouteValueDictionary? currentRouteValues)
    {
        if (!string.IsNullOrWhiteSpace(currentPage) &&
            currentRouteValues is not null &&
            currentRouteValues.ContainsKey("owner"))
        {
            var preservedValues = new RouteValueDictionary(currentRouteValues)
            {
                ["owner"] = owner
            };
            preservedValues.Remove("handler");
            return new ConsoleOwnerSwitchTarget(currentPage, preservedValues);
        }

        return ConsoleOwnerSwitchTarget.ForSelectedOwner(new RouteValueDictionary(new { owner }));
    }
}
