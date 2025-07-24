using Dai_Lete.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Dai_Lete.Repositories;

public class RedirectCache
{
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RedirectService _redirectService;
    private readonly ILogger<RedirectCache> _logger;

    public RedirectCache(IMemoryCache cache, IHttpClientFactory httpClientFactory, RedirectService redirectService, ILogger<RedirectCache> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _redirectService = redirectService ?? throw new ArgumentNullException(nameof(redirectService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> GetHtmlAsync(Guid id)
    {
        _logger.LogInformation("RedirectCache: Looking for redirect with ID: {Id}", id);
        string cacheKey = $"redirect_html_{id}";

        if (!_cache.TryGetValue(cacheKey, out string? cachedHtml))
        {
            try
            {
                _logger.LogInformation("RedirectCache: Cache miss, calling RedirectService for ID: {Id}", id);
                var record = await _redirectService.GetRedirectLinkAsync(id);
                _logger.LogInformation("RedirectCache: Found redirect record - ID: {RecordId}, URL: {Url}", record.Id, record.OriginalLink);
                var client = _httpClientFactory.CreateClient();
                cachedHtml = await client.GetStringAsync(record.OriginalLink);
                _logger.LogInformation("RedirectCache: Successfully fetched content from URL: {Url}", record.OriginalLink);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("RedirectCache: ArgumentException caught - {Message}", ex.Message);
                return "<html><body><h1>Redirect not found</h1></body></html>";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RedirectCache: Unexpected error for ID: {Id}", id);
                return "<html><body><h1>Error loading redirect</h1></body></html>";
            }

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromHours(12));

            _cache.Set(cacheKey, cachedHtml, cacheOptions);
        }
        else
        {
            _logger.LogInformation("RedirectCache: Cache hit for ID: {Id}", id);
        }

        return cachedHtml!;
    }
}