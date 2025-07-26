using System.Collections.Concurrent;
using System.Xml;
using Dai_Lete.Models;

namespace Dai_Lete.Services;

public class FeedCacheService
{
    private readonly XmlService _xmlService;
    private readonly PodcastServices _podcastServices;
    private readonly ILogger<FeedCacheService> _logger;

    public IDictionary<Guid, XmlDocument> FeedCache { get; } = new ConcurrentDictionary<Guid, XmlDocument>();
    public IDictionary<Guid, PodcastMetadata> MetaDataCache { get; } = new ConcurrentDictionary<Guid, PodcastMetadata>();

    public FeedCacheService(XmlService xmlService, PodcastServices podcastServices, ILogger<FeedCacheService> logger)
    {
        _xmlService = xmlService ?? throw new ArgumentNullException(nameof(xmlService));
        _podcastServices = podcastServices ?? throw new ArgumentNullException(nameof(podcastServices));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task UpdatePodcastCacheAsync(Guid id)
    {
        try
        {
            _logger.LogInformation("Starting cache update for podcast {PodcastId}", id);
            var feed = await _xmlService.GenerateNewFeedAsync(id);
            FeedCache[id] = feed;
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
        MetaDataCache[id] = podcastMetadata;
        _logger.LogInformation("Updated metadata cache for podcast {PodcastId} - ProcessedEpisodes: {ProcessedCount}, NonProcessedEpisodes: {NonProcessedCount}", 
            id, podcastMetadata.processedEpisodes?.Count ?? 0, podcastMetadata.nonProcessedEpisodes?.Count ?? 0);
        return Task.CompletedTask;
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

            _logger.LogInformation("Feed cache build COMPLETED FINE with {Count} items, {ProcessedCount}/{TotalCount} podcasts processed", 
                FeedCache.Count, processedCount, podcasts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build feed cache");
            throw;
        }
    }
}