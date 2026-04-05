namespace Makana.WebConsole.Models;

public static class ConsoleRoutePaths
{
    public static string Splash()
        => "/";

    public static string Login(bool showProviders = false)
        => showProviders ? "/login?showProviders=true" : "/login";

    public static string Reauthenticate()
        => "/reauthenticate";

    public static string OwnerDashboard(string owner)
        => $"/{Escape(owner)}";

    public static string Stack(string owner, string project, string stack)
        => $"{OwnerDashboard(owner)}/stacks/{Escape(project)}/{Escape(stack)}";

    public static string StackUpdateVersion(string owner, string project, string stack, int version)
        => $"{Stack(owner, project, stack)}/updates/{version}";

    public static string StackRun(string owner, string project, string stack, string? runType, string? updateRef)
    {
        var stackPath = Stack(owner, project, stack);
        if (string.IsNullOrWhiteSpace(runType) || string.IsNullOrWhiteSpace(updateRef))
        {
            return stackPath;
        }

        return $"{stackPath}/{Escape(runType)}/{Escape(updateRef)}";
    }

    public static string? ResolveOwnerFromLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) ||
            returnUrl[0] != '/')
        {
            return null;
        }

        var queryIndex = returnUrl.IndexOf('?');
        var fragmentIndex = returnUrl.IndexOf('#');
        var segmentEnd = queryIndex switch
        {
            >= 0 when fragmentIndex >= 0 => Math.Min(queryIndex, fragmentIndex),
            >= 0 => queryIndex,
            _ when fragmentIndex >= 0 => fragmentIndex,
            _ => returnUrl.Length
        };

        var path = returnUrl[..segmentEnd];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var owner = Uri.UnescapeDataString(segments[0]);
        return IsGlobalRouteSegment(owner) ? null : owner;
    }

    private static bool IsGlobalRouteSegment(string segment)
        => segment.Equals("account", StringComparison.OrdinalIgnoreCase) ||
           segment.Equals("api", StringComparison.OrdinalIgnoreCase) ||
           segment.Equals("cli-login", StringComparison.OrdinalIgnoreCase) ||
           segment.Equals("login", StringComparison.OrdinalIgnoreCase) ||
           segment.Equals("reauthenticate", StringComparison.OrdinalIgnoreCase) ||
           segment.Equals("welcome", StringComparison.OrdinalIgnoreCase) ||
           segment.StartsWith("__", StringComparison.Ordinal);

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
