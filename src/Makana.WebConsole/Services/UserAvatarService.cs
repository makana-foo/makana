using Microsoft.Extensions.Caching.Memory;
using Solacon;

namespace Makana.WebConsole.Services;

public sealed class UserAvatarService(IMemoryCache memoryCache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(30);

    public string GetSvg(string userId, string? title)
    {
        var cacheKey = $"avatar:{userId}:{title ?? string.Empty}";
        return memoryCache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return SolaconGenerator.GenerateSvg(
                seed: userId,
                rgb: string.Empty,
                includeTitle: !string.IsNullOrWhiteSpace(title),
                title: title ?? string.Empty);
        })!;
    }
}
