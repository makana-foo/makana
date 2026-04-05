using System.Runtime.InteropServices;

namespace Makana.AppHost;

internal static class AppHostContainerUsers
{
    public static readonly ContainerUserIds Api = new(1654, 1654);
    public static readonly ContainerUserIds Oidc = new(1001, 1001);

    public static ContainerUserIds ResolveCurrentProcessOrDefault(ContainerUserIds fallback)
    {
        if (OperatingSystem.IsWindows())
        {
            return fallback;
        }

        try
        {
            return new ContainerUserIds(checked((int)GetEffectiveUserId()), checked((int)GetEffectiveGroupId()));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or OverflowException)
        {
            return fallback;
        }
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [DllImport("libc", EntryPoint = "getegid")]
    private static extern uint GetEffectiveGroupId();
}

internal readonly record struct ContainerUserIds(int UserId, int GroupId)
{
    public string UserSpec => $"{UserId}:{GroupId}";
}
