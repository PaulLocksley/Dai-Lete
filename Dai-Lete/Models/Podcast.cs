using System.Runtime.InteropServices;

namespace Dai_Lete.Models;


public class Podcast
{
    public Uri InUri;
    public Guid Id;
    public PodcastSettings? PodcastSettings;
    public string? PodcastName;
    
    public Podcast(Uri inUri)
    {
        InUri = inUri;
        Id = Guid.NewGuid();
        PodcastSettings = new PodcastSettings(new List<string>());

    }
    public Podcast(string id, string inUri)
    {
        InUri = new Uri(inUri);
        Id = new Guid(id);
    }
    public Podcast(string id, string inUri, PodcastSettings podcastSettings)
    {
        InUri = new Uri(inUri);
        Id = new Guid(id);
        PodcastSettings = podcastSettings;
    }
    public override string ToString()
    {
        return $"{Id.ToString()}, {PodcastName} {InUri}";
    }
}