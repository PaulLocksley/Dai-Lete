using System.Collections.Concurrent;
using Dai_Lete.Models;

namespace Dai_Lete.Repositories;

public static class PodcastQueue
{
    public static ConcurrentQueue<(Podcast podcast, string episodeUrl,string episodeGuid)> toProcessQueue = new();
    
}