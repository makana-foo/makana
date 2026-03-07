using Makana.StackUpdates.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Makana.Api.Updates;

internal sealed class UpdateProjectionStateCache(IMemoryCache cache)
{
    private static string CacheKey(string updateId, long sequence) => $"update-projection-state:{updateId}:{sequence}";

    public bool TryGet(string updateId, long sequence, out UpdateProjectionReducer.ProjectionState? state)
    {
        if (cache.TryGetValue<UpdateProjectionReducer.ProjectionState>(CacheKey(updateId, sequence), out var cached) &&
            cached is not null)
        {
            state = cached.Clone();
            return true;
        }

        state = null;
        return false;
    }

    public void Set(string updateId, long sequence, UpdateProjectionReducer.ProjectionState state)
    {
        cache.Set(
            CacheKey(updateId, sequence),
            state.Clone(),
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(15),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2),
                Size = 1
            });
    }
}
