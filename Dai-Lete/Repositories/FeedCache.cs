using System.Collections.Concurrent;
using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.ScheduledTasks;

namespace Dai_Lete.Repositories;

public static class FeedCache
{
    //todo mark fields private.
    public static IDictionary<Guid, XmlDocument> feedCache = new ConcurrentDictionary<Guid, XmlDocument>();
    public static IDictionary<Guid, PodcastMetadata> metaDataCache = new ConcurrentDictionary<Guid, PodcastMetadata>();
    public static async Task updateCache(Guid id)
    {
        feedCache[id] = XmlService.GenerateNewFeed(id);
    }

    public static async Task updateMetaData(Guid id, PodcastMetadata podcastMetadata)
    {
        metaDataCache[id] = podcastMetadata;
    }
    public static void buildCache()
    {
        var plist = PodcastServices.GetPodcasts();
        foreach (var podcast in plist)
        {
            updateCache(podcast.Id);
        }
        Console.WriteLine($"Feeds buld, cache contains {feedCache.Count} items");
    }
     
}