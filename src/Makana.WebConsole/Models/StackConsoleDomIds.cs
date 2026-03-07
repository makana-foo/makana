using System.Security.Cryptography;
using System.Text;

namespace Makana.WebConsole.Models;

public static class StackConsoleDomIds
{
    public static string For(string view, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{view}:{key}"));
        return $"live-update-{view}-line-{Convert.ToHexString(bytes[..8]).ToLowerInvariant()}";
    }
}
