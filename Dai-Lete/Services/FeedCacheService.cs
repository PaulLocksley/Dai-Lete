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
            var feed = await _xmlService.GenerateNewFeedAsync(id);
            FeedCache[id] = feed;
            _logger.LogDebug("Updated cache for podcast {PodcastId}", id);
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
        _logger.LogDebug("Updated metadata cache for podcast {PodcastId}", id);
        return Task.CompletedTask;
    }

    public async Task BuildCacheAsync()
    {
        try
        {
            _logger.LogInformation("Building feed cache");
            var podcasts = await _podcastServices.GetPodcastsAsync();
            
            foreach (var podcast in podcasts)
            {
                try
                {
                    await UpdatePodcastCacheAsync(podcast.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to build cache for podcast {PodcastId}", podcast.Id);
                }
            }
            
            _logger.LogInformation("Feed cache built with {Count} items", FeedCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build feed cache");
            throw;
        }
    }
}