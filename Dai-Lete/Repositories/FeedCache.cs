using System.Collections.Concurrent;
using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Services;

namespace Dai_Lete.Repositories;

public static class FeedCache
{
    private static FeedCacheService? _feedCacheService;
    
    public static IDictionary<Guid, XmlDocument> feedCache => _feedCacheService?.FeedCache ?? new ConcurrentDictionary<Guid, XmlDocument>();
    public static IDictionary<Guid, PodcastMetadata> metaDataCache => _feedCacheService?.MetaDataCache ?? new ConcurrentDictionary<Guid, PodcastMetadata>();

    public static void Initialize(FeedCacheService feedCacheService)
    {
        _feedCacheService = feedCacheService ?? throw new ArgumentNullException(nameof(feedCacheService));
    }

    public static async Task UpdatePodcastCache(Guid id)
    {
        if (_feedCacheService is null) throw new InvalidOperationException("FeedCache not initialized");
        await _feedCacheService.UpdatePodcastCacheAsync(id);
    }

    public static async Task updateMetaData(Guid id, PodcastMetadata podcastMetadata)
    {
        if (_feedCacheService is null) throw new InvalidOperationException("FeedCache not initialized");
        await _feedCacheService.UpdateMetaDataAsync(id, podcastMetadata);
    }

    public static async Task buildCache()
    {
        if (_feedCacheService is null) throw new InvalidOperationException("FeedCache not initialized");
        await _feedCacheService.BuildCacheAsync();
    }
    
}