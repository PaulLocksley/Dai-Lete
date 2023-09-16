using System.Runtime.InteropServices;

namespace Dai_Lete.Models;


[Serializable]
public class Podcast
{
    public Uri InUri;
    public string OutUri;
    public Guid Id;
    
    public Podcast(Uri inUri)
    {
        InUri = inUri;
        Id = Guid.NewGuid();
        OutUri = $"/{Id}/";

    }
    

    public Podcast(String id, String inUri, String outUri )
    {
        InUri = new Uri(inUri);
        OutUri = outUri;
        Id = Guid.Parse(id);
    }

    public Dictionary<Uri,Uri> GetEpisodes()
    {
        var EpisodesDict = new Dictionary<Uri, Uri>();



        return EpisodesDict;
    }

    public override string ToString()
    {
        return $"{Id.ToString()}, {InUri}, {OutUri}";
    }
}