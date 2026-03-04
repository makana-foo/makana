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

    public async Task<StackDetails?> GetStackAsync(string org, string project, string stack, CancellationToken cancellationToken)
    {
        var path = $"/api/stacks/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(stack)}";
        return await httpClient.GetFromJsonAsync<StackDetails>(path, JsonOptions, cancellationToken);
    }

    public async Task<GetHistoryResponse?> GetStackUpdatesAsync(string org, string project, string stack, CancellationToken cancellationToken)
    {
        var path = $"/api/stacks/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(stack)}/updates";
        return await httpClient.GetFromJsonAsync<GetHistoryResponse>(path, JsonOptions, cancellationToken);
    }

    public async Task<LatestUpdateInfo?> GetLatestUpdateAsync(string org, string project, string stack, CancellationToken cancellationToken)
    {
        var path = $"/api/stacks/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(stack)}/updates/latest";
        return await httpClient.GetFromJsonAsync<LatestUpdateInfo>(path, JsonOptions, cancellationToken);
    }

    public async Task<UpdateResults?> GetUpdateResultsAsync(
        string org,
        string project,
        string stack,
        string updateId,
        CancellationToken cancellationToken)
    {
        var path = $"/api/stacks/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(stack)}/update/{Uri.EscapeDataString(updateId)}";
        return await httpClient.GetFromJsonAsync<UpdateResults>(path, JsonOptions, cancellationToken);
    }

    public async Task<GetUpdateEventsResponse?> GetUpdateEventsAsync(
        string org,
        string project,
        string stack,
        string updateId,
        CancellationToken cancellationToken)
    {
        var path = $"/api/stacks/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(stack)}/update/{Uri.EscapeDataString(updateId)}/events";
        return await httpClient.GetFromJsonAsync<GetUpdateEventsResponse>(path, JsonOptions, cancellationToken);
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
               configuration["PULUMI_API_INTERNAL_ENDPOINT"] ??
               "http://localhost:8080";
    }

    private static string GetApiBaseUrl(IConfiguration configuration)
    {
        return configuration["PULUMI_API_INTERNAL_ENDPOINT"] ??
               configuration["PULUMI_API"] ??
               "http://localhost:8080";
    }
}
