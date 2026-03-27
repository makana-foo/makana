namespace Makana.WebConsole.Models;

public sealed record WebConsoleLoginProvider(
    string Id,
    string BadgeText,
    string DisplayName,
    string ButtonLabel,
    string ChallengeValue);
