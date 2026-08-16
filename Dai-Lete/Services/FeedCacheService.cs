using System.Text.Json;
using Dai_Lete.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Dai_Lete.Services;

public class FeedCacheService
{
    private readonly XmlService _xmlService;
    private readonly PodcastServices _podcastServices;
    private readonly ILogger<FeedCacheService> _logger;
    private readonly IDatabase _database;
    private readonly ValkeyOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new() { IncludeFields = true };

    public FeedCacheService(XmlService xmlService, PodcastServices podcastServices, ILogger<FeedCacheService> logger,
        IConnectionMultiplexer redis, IOptions<ValkeyOptions> options)
    {
        _xmlService = xmlService ?? throw new ArgumentNullException(nameof(xmlService));
        _podcastServices = podcastServices ?? throw new ArgumentNullException(nameof(podcastServices));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _database = redis?.GetDatabase() ?? throw new ArgumentNullException(nameof(redis));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    private string FeedKey(Guid id) => $"{_options.FeedCachePrefix}:{id}";
    private string MetadataKey(Guid id) => $"{_options.MetadataCachePrefix}:{id}";
    private TimeSpan CacheExpiration => TimeSpan.FromMinutes(_options.FeedCacheExpirationMinutes);

    public async Task<string?> GetPodcastFeedXmlAsync(Guid id)
    {
        var value = await _database.StringGetAsync(FeedKey(id));
        return value.HasValue ? value.ToString() : null;
    }

    public async Task<bool> HasPodcastFeedAsync(Guid id)
    {
        return await _database.KeyExistsAsync(FeedKey(id));
    }

    public async Task UpdatePodcastCacheAsync(Guid id)
    {
        try
        {
            _logger.LogInformation("Starting cache update for podcast {PodcastId}", id);
            var feed = await _xmlService.GenerateNewFeedAsync(id);
            await _database.StringSetAsync(FeedKey(id), feed.OuterXml, CacheExpiration);
            _logger.LogInformation("Successfully updated cache for podcast {PodcastId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update cache for podcast {PodcastId}", id);
            throw;
        }
    }

    public Task UpdateMetaDataAsync(Guid id, PodcastMetadata podcastMetadata)
    {
        var json = JsonSerializer.Serialize(podcastMetadata, _jsonOptions);
        _logger.LogInformation("Updated metadata cache for podcast {PodcastId} - ProcessedEpisodes: {ProcessedCount}, NonProcessedEpisodes: {NonProcessedCount}", 
            id, podcastMetadata.processedEpisodes?.Count ?? 0, podcastMetadata.nonProcessedEpisodes?.Count ?? 0);
        return _database.StringSetAsync(MetadataKey(id), json, CacheExpiration);
    }

    public async Task<PodcastMetadata?> GetMetaDataAsync(Guid id)
    {
        var value = await _database.StringGetAsync(MetadataKey(id));
        return value.HasValue ? JsonSerializer.Deserialize<PodcastMetadata>(value.ToString(), _jsonOptions) : null;
    }

    public async Task<IReadOnlyDictionary<Guid, PodcastMetadata>> GetAllMetaDataAsync()
    {
        var podcasts = await _podcastServices.GetPodcastsAsync();
        var metadata = new Dictionary<Guid, PodcastMetadata>();

        foreach (var podcast in podcasts)
        {
            var item = await GetMetaDataAsync(podcast.Id);
            if (item is null)
            {
                try
                {
                    await UpdatePodcastCacheAsync(podcast.Id);
                    item = await GetMetaDataAsync(podcast.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to build metadata for podcast {PodcastId}", podcast.Id);
                }
            }

            if (item is not null)
            {
                metadata[podcast.Id] = item.Value;
            }
        }

        return metadata;
    }

    public async Task RemovePodcastCacheAsync(Guid id)
    {
        await _database.KeyDeleteAsync(new RedisKey[] { FeedKey(id), MetadataKey(id) });
    }

    public async Task BuildCacheAsync()
    {
        try
        {
            _logger.LogInformation("Feed cache build STARTED");
            var podcasts = await _podcastServices.GetPodcastsAsync();
            _logger.LogInformation("Found {PodcastCount} podcasts to process in cache build", podcasts.Count);

            var processedCount = 0;
            foreach (var podcast in podcasts)
            {
                try
                {
                    _logger.LogInformation("Processing podcast {PodcastId} ({Current}/{Total}) in cache build", 
                        podcast.Id, processedCount + 1, podcasts.Count);
                    await UpdatePodcastCacheAsync(podcast.Id);
                    processedCount++;
                    _logger.LogInformation("Successfully processed podcast {PodcastId} ({Current}/{Total})", 
                        podcast.Id, processedCount, podcasts.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to build cache for podcast {PodcastId}", podcast.Id);
                    processedCount++;
                }
            }

            _logger.LogInformation("Feed cache build COMPLETED FINE with {ProcessedCount}/{TotalCount} podcasts processed",
                processedCount, podcasts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build feed cache");
            throw;
        }
    }
}
