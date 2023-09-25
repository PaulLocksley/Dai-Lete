using System.Runtime.InteropServices;

namespace Dai_Lete.Models;


[Serializable]
public class Podcast
{
    public Uri InUri;
    public Guid Id;
    
    public Podcast(Uri inUri)
    {
        InUri = inUri;
        Id = Guid.NewGuid();

    }

    public Podcast(string id, string inUri)
    {
        InUri = new Uri(inUri);
        Id = new Guid(id);
    }
    public Dictionary<Uri,Uri> GetEpisodes()
    {
        var EpisodesDict = new Dictionary<Uri, Uri>();



        return EpisodesDict;
    }

    public override string ToString()
    {
        return $"{Id.ToString()}, {InUri}";
    }
}