using Dai_Lete.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Dai_Lete.Repositories;

public class RedirectCache(IMemoryCache cache, IHttpClientFactory httpClientFactory)
{
    public async Task<string> GetHtmlAsync(Guid id)
    {
        string cacheKey = $"redirect_html_{id}";

        if (!cache.TryGetValue(cacheKey, out string? cachedHtml))
        {
            var record = RedirectService.GetRedirectLink(id);
            var client = httpClientFactory.CreateClient();
            cachedHtml = await client.GetStringAsync(record.OriginalLink);

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromHours(12));

            cache.Set(cacheKey, cachedHtml, cacheOptions);
        }

        return cachedHtml!;
    }
}