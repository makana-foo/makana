namespace Makana.WebConsole.Models;

public static class ConsoleRoutePaths
{
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

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
