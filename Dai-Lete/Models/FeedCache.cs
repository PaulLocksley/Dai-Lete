using System.Collections.Concurrent;
using System.Xml;
using Dai_Lete.ScheduledTasks;

namespace Dai_Lete.Models;

public static class FeedCache
{
    
    public static IDictionary<Guid, XmlDocument> feedCache = new ConcurrentDictionary<Guid, XmlDocument>();

    public static async Task updateCache(Guid id)
    {
        feedCache[id] = XmlService.GenerateNewFeed(id);
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