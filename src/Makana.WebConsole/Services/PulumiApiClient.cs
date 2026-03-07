using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Makana.WebConsole.Models;

namespace Makana.WebConsole.Services;

public sealed class PulumiApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServiceUser?> GetUserAsync(CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<ServiceUser>("/api/user", JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<StackSummary>> GetUserStacksAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.GetFromJsonAsync<ListStacksResponse>("/api/user/stacks", JsonOptions, cancellationToken);
        return response?.Stacks ?? [];
    }

    public async Task<IReadOnlyList<StackSummary>> GetOrganizationStacksAsync(string organization, CancellationToken cancellationToken)
    {
        var path = $"/api/stacks/{Uri.EscapeDataString(organization)}";
        var response = await httpClient.GetFromJsonAsync<ListStacksResponse>(path, JsonOptions, cancellationToken);
        return response?.Stacks ?? [];
    }

    public async Task<StackOverviewResponse?> GetStackOverviewAsync(string org, string project, string stack, CancellationToken cancellationToken)
    {
        var path = $"/api/stacks/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(stack)}/overview";
        return await httpClient.GetFromJsonAsync<StackOverviewResponse>(path, JsonOptions, cancellationToken);
    }

    public async Task<SelectedUpdateReferenceResponse?> GetUpdateReferenceByVersionAsync(string org, string project, string stack, int version, CancellationToken cancellationToken)
    {
        var path = $"/api/stacks/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(stack)}/updates/{version}/reference";
        return await httpClient.GetFromJsonAsync<SelectedUpdateReferenceResponse>(path, JsonOptions, cancellationToken);
    }

    public async Task<UpdateViewResponse?> GetUpdateViewAsync(
        string org,
        string project,
        string stack,
        string updateId,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var path = $"/api/stacks/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(stack)}/update/{Uri.EscapeDataString(updateId)}/view";
        if (!string.IsNullOrWhiteSpace(continuationToken))
        {
            path += $"?continuationToken={Uri.EscapeDataString(continuationToken)}";
        }

        return await httpClient.GetFromJsonAsync<UpdateViewResponse>(path, JsonOptions, cancellationToken);
    }

    public static void ConfigureHttpClient(HttpClient client, IConfiguration configuration)
    {
        var baseUrl = GetApiBaseUrl(configuration);

        client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.pulumi+8"));
    }

    public static string GetBrowserApiBaseUrl(IConfiguration configuration)
    {
        return configuration["PULUMI_API"] ??
               "http://localhost:8080";
    }

    private static string GetApiBaseUrl(IConfiguration configuration)
    {
        return configuration["PULUMI_API_SERVICE"] ??
               "http://api";
    }
}
