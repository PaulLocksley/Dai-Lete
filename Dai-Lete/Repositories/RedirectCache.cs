using Dai_Lete.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Dai_Lete.Repositories;

public class RedirectCache
{
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RedirectService _redirectService;

    public RedirectCache(IMemoryCache cache, IHttpClientFactory httpClientFactory, RedirectService redirectService)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _redirectService = redirectService ?? throw new ArgumentNullException(nameof(redirectService));
    }

    public async Task<string> GetHtmlAsync(Guid id)
    {
        string cacheKey = $"redirect_html_{id}";

        if (!_cache.TryGetValue(cacheKey, out string? cachedHtml))
        {
            var record = await _redirectService.GetRedirectLinkAsync(id);
            var client = _httpClientFactory.CreateClient();
            cachedHtml = await client.GetStringAsync(record.OriginalLink);

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromHours(12));

            _cache.Set(cacheKey, cachedHtml, cacheOptions);
        }

        return cachedHtml!;
    }
}