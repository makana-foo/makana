using Makana.Api.Storage;

namespace Makana.Api.Security;

internal static class NamespaceRoutingHelpers
{
    public static bool IsLoginCollision(string orgLogin, string userLogin)
        => string.Equals(orgLogin, userLogin, StringComparison.OrdinalIgnoreCase);

    public static string GetCollisionOrgAlias(string orgLogin, string orgId)
    {
        var idPrefix = orgId.Length >= 8 ? orgId[..8] : orgId;
        return $"org-{idPrefix}-{orgLogin}";
    }

    public static async Task<string?> ResolveMemberOrgIdFromRouteAsync(
        BackendStore store,
        string userId,
        string userLogin,
        string routeOrg)
    {
        var orgs = await store.GetUserOrgsAsync(userId);
        foreach (var org in orgs)
        {
            if (string.Equals(org.Login, routeOrg, StringComparison.OrdinalIgnoreCase))
            {
                return org.Id;
            }

            if (IsLoginCollision(org.Login, userLogin) &&
                string.Equals(GetCollisionOrgAlias(org.Login, org.Id), routeOrg, StringComparison.OrdinalIgnoreCase))
            {
                return org.Id;
            }
        }

        return null;
    }
}
