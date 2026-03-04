using Makana.Api.Storage;

namespace Makana.Api.Security;

internal static class NamespaceRoutingHelpers
{
    public static async Task<string?> ResolveMemberOrgIdFromRouteAsync(
        BackendStore store,
        string userId,
        string routeOwner)
    {
        var orgs = await store.GetUserOrgsAsync(userId);
        foreach (var org in orgs)
        {
            if (string.Equals(org.Login, routeOwner, StringComparison.OrdinalIgnoreCase))
            {
                return org.Id;
            }
        }

        return null;
    }

    public static async Task<string?> ResolveAccessibleNamespaceIdAsync(
        BackendStore store,
        string userId,
        string routeOwner,
        string? tokenOrgId)
    {
        var user = await store.GetUserByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(tokenOrgId))
        {
            var tokenOrgLogin = await store.GetOrgLoginByIdAsync(tokenOrgId);
            return !string.IsNullOrWhiteSpace(tokenOrgLogin) &&
                   string.Equals(tokenOrgLogin, routeOwner, StringComparison.OrdinalIgnoreCase)
                ? tokenOrgId
                : null;
        }

        if (string.Equals(user.Value.Login, routeOwner, StringComparison.OrdinalIgnoreCase))
        {
            return userId;
        }

        return await ResolveMemberOrgIdFromRouteAsync(store, userId, routeOwner);
    }
}
