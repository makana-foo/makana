using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Makana.AppHost.PocketId;

internal static class PocketIdOidcClientConfigurator
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private const int MaxAttempts = 20;

    public static async Task<string> EnsureAsync(
        HttpClient client,
        string staticApiKey,
        string clientId,
        bool isPublic,
        Uri launchUrl,
        Uri callbackUrl,
        Uri logoutCallbackUrl,
        CancellationToken cancellationToken)
    {
        client.DefaultRequestHeaders.Remove("X-API-KEY");
        client.DefaultRequestHeaders.Add("X-API-KEY", staticApiKey);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var updateResponse = await client.PutAsJsonAsync(
                    $"/api/oidc/clients/{clientId}",
                    new UpdateOidcClientRequest(
                        clientId,
                        "Makana Dex Upstream",
                        isPublic,
                        true,
                        launchUrl.ToString().TrimEnd('/'),
                        [callbackUrl.ToString()],
                        [logoutCallbackUrl.ToString()]),
                    cancellationToken);
                if (updateResponse.IsSuccessStatusCode)
                {
                    return await CreateClientSecretAsync(client, clientId, cancellationToken);
                }

                if (updateResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    await EnsureClientExistsAsync(client, clientId, cancellationToken);

                    using var retryResponse = await client.PutAsJsonAsync(
                        $"/api/oidc/clients/{clientId}",
                        new UpdateOidcClientRequest(
                            clientId,
                            "Makana Dex Upstream",
                            isPublic,
                            true,
                            launchUrl.ToString().TrimEnd('/'),
                            [callbackUrl.ToString()],
                            [logoutCallbackUrl.ToString()]),
                        cancellationToken);
                    if (retryResponse.IsSuccessStatusCode)
                    {
                        return await CreateClientSecretAsync(client, clientId, cancellationToken);
                    }

                    var retryBody = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
                    throw new HttpRequestException(
                        $"Pocket ID OIDC client update retry failed with status {(int)retryResponse.StatusCode}: {retryBody}",
                        null,
                        retryResponse.StatusCode);
                }

                var responseBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"Pocket ID OIDC client update failed with status {(int)updateResponse.StatusCode}: {responseBody}",
                    null,
                    updateResponse.StatusCode);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        throw new HttpRequestException(
            $"Pocket ID OIDC client provisioning did not succeed after {MaxAttempts} attempts for client '{clientId}'.");
    }

    private static async Task EnsureClientExistsAsync(
        HttpClient client,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var createResponse = await client.PostAsJsonAsync(
            "/api/oidc/clients",
            new CreateOidcClientRequest(clientId, "Makana Dex Upstream"),
            cancellationToken);
        if (createResponse.IsSuccessStatusCode)
        {
            return;
        }

        var responseBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Pocket ID OIDC client create failed with status {(int)createResponse.StatusCode}: {responseBody}",
            null,
            createResponse.StatusCode);
    }

    private static async Task<string> CreateClientSecretAsync(
        HttpClient client,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var secretResponse = await client.PostAsync(
            $"/api/oidc/clients/{clientId}/secret",
            content: null,
            cancellationToken);
        if (!secretResponse.IsSuccessStatusCode)
        {
            var responseBody = await secretResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Pocket ID OIDC client secret creation failed with status {(int)secretResponse.StatusCode}: {responseBody}",
                null,
                secretResponse.StatusCode);
        }

        var secretPayload = await secretResponse.Content.ReadFromJsonAsync<ClientSecretResponse>(cancellationToken);
        if (secretPayload is null || string.IsNullOrWhiteSpace(secretPayload.Secret))
        {
            throw new HttpRequestException($"Pocket ID OIDC client secret creation returned an empty secret for client '{clientId}'.");
        }

        return secretPayload.Secret;
    }

    private sealed record CreateOidcClientRequest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record UpdateOidcClientRequest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("isPublic")] bool IsPublic,
        [property: JsonPropertyName("pkceEnabled")] bool PkceEnabled,
        [property: JsonPropertyName("launchURL")] string LaunchUrl,
        [property: JsonPropertyName("callbackURLs")] IReadOnlyList<string> CallbackUrls,
        [property: JsonPropertyName("logoutCallbackURLs")] IReadOnlyList<string> LogoutCallbackUrls);

    private sealed record ClientSecretResponse(
        [property: JsonPropertyName("secret")] string Secret);

}
