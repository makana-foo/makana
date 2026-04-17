using Microsoft.AspNetCore.Authentication;
using System.Globalization;

namespace Makana.WebConsole.Services;

public static class WebConsoleAuthenticationState
{
    public const string SignedInOwnersItemKey = "makana.auth.signedinowners";
    private const string CarriedSignedInOwnersItemKey = "makana.auth.carry.signedinowners";
    private const string CarriedApiTokenItemKey = "makana.auth.carry.api_token";
    private const string CarriedApiTokenExpiresAtItemKey = "makana.auth.carry.api_token_expires_at";
    private static readonly char[] SignedInOwnersSeparators = [','];
    private static readonly TimeSpan ApiTokenExpirationSkew = TimeSpan.FromMinutes(1);

    public static IReadOnlySet<string> GetSignedInOwners(AuthenticationProperties? properties)
    {
        if (properties?.Items.TryGetValue(SignedInOwnersItemKey, out var serialized) != true ||
            string.IsNullOrWhiteSpace(serialized))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return serialized
            .Split(SignedInOwnersSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsOwnerSignedIn(AuthenticationProperties? properties, string? owner)
        => !string.IsNullOrWhiteSpace(owner) && GetSignedInOwners(properties).Contains(owner);

    public static void MarkOwnerSignedIn(AuthenticationProperties properties, string? owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return;
        }

        var signedInOwners = GetSignedInOwners(properties).ToHashSet(StringComparer.OrdinalIgnoreCase);
        signedInOwners.Add(owner);
        properties.Items[SignedInOwnersItemKey] = string.Join(",", signedInOwners.Order(StringComparer.OrdinalIgnoreCase));
    }

    public static void CarryForwardCurrentSession(AuthenticationProperties challengeProperties, AuthenticationProperties? currentProperties)
    {
        if (currentProperties is null)
        {
            return;
        }

        if (currentProperties.Items.TryGetValue(SignedInOwnersItemKey, out var signedInOwners) &&
            !string.IsNullOrWhiteSpace(signedInOwners))
        {
            challengeProperties.Items[CarriedSignedInOwnersItemKey] = signedInOwners;
        }

        var apiToken = currentProperties.GetTokenValue(WebConsoleApiTokenAccessor.ApiTokenName);
        var apiTokenExpiresAt = currentProperties.GetTokenValue(WebConsoleApiTokenAccessor.ApiTokenExpiresAtName);
        if (string.IsNullOrWhiteSpace(apiToken) ||
            !DateTimeOffset.TryParse(
                apiTokenExpiresAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var expiresAt) ||
            expiresAt <= DateTimeOffset.UtcNow.Add(ApiTokenExpirationSkew))
        {
            return;
        }

        challengeProperties.Items[CarriedApiTokenItemKey] = apiToken;
        challengeProperties.Items[CarriedApiTokenExpiresAtItemKey] = expiresAt.ToString("O", CultureInfo.InvariantCulture);
    }

    public static void RestoreCarriedSession(AuthenticationProperties properties)
    {
        if (properties.Items.TryGetValue(CarriedSignedInOwnersItemKey, out var carriedSignedInOwners) &&
            !string.IsNullOrWhiteSpace(carriedSignedInOwners))
        {
            var mergedOwners = GetSignedInOwners(properties).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var owner in carriedSignedInOwners.Split(SignedInOwnersSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                mergedOwners.Add(owner);
            }

            properties.Items[SignedInOwnersItemKey] = string.Join(",", mergedOwners.Order(StringComparer.OrdinalIgnoreCase));
        }
    }

    public static string? GetCarriedApiToken(AuthenticationProperties? properties)
    {
        if (properties?.Items.TryGetValue(CarriedApiTokenItemKey, out var apiToken) != true ||
            string.IsNullOrWhiteSpace(apiToken) ||
            properties.Items.TryGetValue(CarriedApiTokenExpiresAtItemKey, out var expiresAtValue) != true ||
            !DateTimeOffset.TryParse(
                expiresAtValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var expiresAt) ||
            expiresAt <= DateTimeOffset.UtcNow.Add(ApiTokenExpirationSkew))
        {
            return null;
        }

        return apiToken;
    }

    public static void ClearCarriedApiToken(AuthenticationProperties? properties)
    {
        properties?.Items.Remove(CarriedApiTokenItemKey);
        properties?.Items.Remove(CarriedApiTokenExpiresAtItemKey);
    }

    public static bool HasCarriedSession(AuthenticationProperties? properties)
    {
        if (properties is null)
        {
            return false;
        }

        return properties.Items.ContainsKey(CarriedSignedInOwnersItemKey) ||
               properties.Items.ContainsKey(CarriedApiTokenItemKey);
    }

    public static bool TryGetCurrentApiTokenInfo(AuthenticationProperties? properties, out string token, out string expiresAt)
    {
        token = string.Empty;
        expiresAt = string.Empty;

        if (properties is null)
        {
            return false;
        }

        token = properties.GetTokenValue(WebConsoleApiTokenAccessor.ApiTokenName) ?? string.Empty;
        expiresAt = properties.GetTokenValue(WebConsoleApiTokenAccessor.ApiTokenExpiresAtName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token) ||
            !DateTimeOffset.TryParse(
                expiresAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var expiresAtValue) ||
            expiresAtValue <= DateTimeOffset.UtcNow.Add(ApiTokenExpirationSkew))
        {
            token = string.Empty;
            expiresAt = string.Empty;
            return false;
        }

        return true;
    }
}
