using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace Makana.WebConsole.Services;

public sealed class PulumiApiAuthHeaderHandler(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = null;

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            var jwt = await httpContext.GetTokenAsync("id_token")
                ?? await httpContext.GetTokenAsync("access_token");

            if (!string.IsNullOrWhiteSpace(jwt))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            }
        }

        if (request.Headers.Authorization is null)
        {
            var apiToken = configuration["MAKANA_WEBCONSOLE_API_TOKEN"];
            if (!string.IsNullOrWhiteSpace(apiToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("token", apiToken);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
