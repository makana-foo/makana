using System.Net.Http.Headers;

namespace Makana.WebConsole.Services;

internal sealed class PulumiApiAuthHeaderHandler(WebConsoleApiTokenAccessor apiTokenAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = null;

        var apiToken = await apiTokenAccessor.GetApiTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(apiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("token", apiToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
