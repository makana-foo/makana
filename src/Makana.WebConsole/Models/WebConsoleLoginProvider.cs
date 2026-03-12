namespace Makana.WebConsole.Models;

public sealed record WebConsoleLoginProvider(
    string Id,
    string BadgeText,
    string DisplayName,
    string ButtonLabel)
{
    public const string AuthenticationProviderItemKey = "makana.auth.provider";
    public const string ChallengeProviderParameterName = "provider";

    public static IReadOnlyList<WebConsoleLoginProvider> All { get; } =
    [
        new("passkey", "PK", "Pocket ID passkey", "Sign in with a passkey"),
        new("google", "G", "Google", "Sign in with Google"),
        new("microsoft", "MS", "Microsoft", "Sign in with Microsoft"),
        new("github", "GH", "GitHub", "Sign in with GitHub"),
        new("apple", "A", "Apple", "Sign in with Apple")
    ];

    public static WebConsoleLoginProvider Default => All[0];

    public static WebConsoleLoginProvider GetOrDefault(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return Default;
        }

        foreach (var provider in All)
        {
            if (string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        return Default;
    }
}
