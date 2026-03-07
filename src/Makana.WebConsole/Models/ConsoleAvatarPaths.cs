namespace Makana.WebConsole.Models;

public static class ConsoleAvatarPaths
{
    public static string? ForUser(ServiceUser? user)
    {
        if (string.IsNullOrWhiteSpace(user?.Id))
        {
            return null;
        }

        return $"/avatars/{Uri.EscapeDataString(user.Id)}.svg";
    }
}
