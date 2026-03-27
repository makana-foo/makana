using Makana.WebConsole.Models;
using System.Text.Json;

namespace Makana.WebConsole.Services;

public sealed class PulumiApiClient(HttpClient httpClient)
{
    private static JsonSerializerOptions JsonOptions => JsonSerializerOptions.Web;

    public async Task<ServiceUser?> GetUserAsync(CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<ServiceUser>("/api/user", JsonOptions, cancellationToken);
    }

    public async Task SetUserThemeAsync(string theme, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(
            "/api/user/theme",
            new UpdateUserThemeRequest(theme),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
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
}
