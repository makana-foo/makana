using Makana.WebConsole.Models;
using System.Net;
using System.Net.Http.Json;
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

    public async Task<CreateOrganizationResponse?> CreateOrganizationAsync(string name, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/orgs",
            new CreateOrganizationRequest(name),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreateOrganizationResponse>(JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationMember>> GetOrganizationMembersAsync(string organization, CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/members";
        var response = await httpClient.GetFromJsonAsync<OrganizationMembersResponse>(path, JsonOptions, cancellationToken);
        return response?.Members ?? [];
    }

    public async Task<IReadOnlyList<OrganizationInvite>> GetOrganizationInvitesAsync(string organization, CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/invites";
        var response = await httpClient.GetFromJsonAsync<OrganizationInvitesResponse>(path, JsonOptions, cancellationToken);
        return response?.Invites ?? [];
    }

    public async Task CreateOrganizationInviteAsync(string organization, string email, string role, CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/invites";
        using var response = await httpClient.PostAsJsonAsync(
            path,
            new CreateOrganizationInviteRequest(email, role),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task RevokeOrganizationInviteAsync(string organization, string inviteId, CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/invites/{Uri.EscapeDataString(inviteId)}";
        using var response = await httpClient.DeleteAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateOrganizationMemberRoleAsync(string organization, string userId, string role, CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/members/{Uri.EscapeDataString(userId)}";
        using var response = await httpClient.PatchAsJsonAsync(
            path,
            new UpdateOrganizationMemberRequest(role),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveOrganizationMemberAsync(string organization, string userId, CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/members/{Uri.EscapeDataString(userId)}";
        using var response = await httpClient.DeleteAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<OrganizationTokenSummary>> GetOrganizationTokensAsync(string organization, CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/tokens";
        var response = await httpClient.GetFromJsonAsync<OrganizationTokensResponse>(path, JsonOptions, cancellationToken);
        return response?.Tokens ?? [];
    }

    public async Task<CreateOrganizationTokenResponse?> CreateOrganizationTokenAsync(
        string organization,
        string name,
        string? description,
        int? expiresInDays,
        CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/tokens";
        using var response = await httpClient.PostAsJsonAsync(
            path,
            new CreateOrganizationTokenRequest(name, description, expiresInDays),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreateOrganizationTokenResponse>(JsonOptions, cancellationToken);
    }

    public async Task RevokeOrganizationTokenAsync(string organization, string tokenId, CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/tokens/{Uri.EscapeDataString(tokenId)}";
        using var response = await httpClient.DeleteAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<OrganizationSsoConfiguration?> GetOrganizationSsoAsync(string organization, CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/auth/sso";
        return await GetOptionalJsonAsync<OrganizationSsoConfiguration>(path, cancellationToken);
    }

    public async Task<OrganizationSsoConfiguration?> UpsertOrganizationSsoAsync(
        string organization,
        UpdateOrganizationSsoRequest request,
        CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/auth/sso";
        using var response = await httpClient.PutAsJsonAsync(path, request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrganizationSsoConfiguration>(JsonOptions, cancellationToken);
    }

    public async Task DeleteOrganizationSsoAsync(string organization, CancellationToken cancellationToken)
    {
        var path = $"/api/orgs/{Uri.EscapeDataString(organization)}/auth/sso";
        using var response = await httpClient.DeleteAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<OrganizationLoginDiscovery?> GetOrganizationLoginAsync(string organization, CancellationToken cancellationToken)
        => GetOptionalJsonAsync<OrganizationLoginDiscovery>(
            $"/api/orgs/{Uri.EscapeDataString(organization)}/auth/login",
            cancellationToken);

    public Task<OrganizationLoginDiscovery?> DiscoverOrganizationLoginAsync(string email, CancellationToken cancellationToken)
        => GetOptionalJsonAsync<OrganizationLoginDiscovery>(
            $"/api/orgs/discover?email={Uri.EscapeDataString(email)}",
            cancellationToken);

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

    private async Task<T?> GetOptionalJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<T>(path, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }
}
