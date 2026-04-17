using Makana.WebConsole.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace Makana.WebConsole.Services;

public sealed class OrganizationSsoReauthenticationService(PulumiApiClient apiClient)
{
    public async Task<string?> GetRedirectUriAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var requestedOwner = httpContext.GetRouteValue("owner")?.ToString();
        if (httpContext.User.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(requestedOwner))
        {
            return null;
        }

        var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (authResult is not { Succeeded: true })
        {
            return null;
        }

        if (WebConsoleAuthenticationState.IsOwnerSignedIn(authResult.Properties, requestedOwner))
        {
            return null;
        }

        var organizationLogin = await apiClient.GetOrganizationLoginAsync(requestedOwner, cancellationToken);
        if (organizationLogin is null)
        {
            return null;
        }

        var returnUrl = httpContext.Request.PathBase.Add(httpContext.Request.Path) + httpContext.Request.QueryString;
        return QueryHelpers.AddQueryString(
            ConsoleRoutePaths.Reauthenticate(),
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["owner"] = requestedOwner,
                ["returnUrl"] = returnUrl
            });
    }
}
