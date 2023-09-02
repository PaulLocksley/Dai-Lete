using System.Runtime.InteropServices;

namespace Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dapper;
using System.Text.Json;

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
        //new Uri($"{Request.Scheme}://{Request.Host}{Request.PathBase}/your/custom/path");

    }

    /*public Podcast(Guid id)
    {
        var sql = @"Select * From Podcasts WHERE id = $id";
        var PossiblePodcast = SqLite.Connection().Query<Podcast>(sql).First();
        if (PossiblePodcast is null)
        {
            throw new FileNotFoundException($"Podcast with ID: {id} not found.");
        }

        Id = id;
        InUri = PossiblePodcast.InUri;
        OutUri = PossiblePodcast.OutUri;
    }*/

    public Podcast(String id, String inUri, String outUri )
    {
        InUri = new Uri(inUri);
        OutUri = outUri;
        Id = Guid.Parse(id);
    }

    public Dictionary<Uri,Uri> GetEpisodes()
    {
        var EpisodesDict = new Dictionary<Uri, Uri>();

                        /*
                        @"
                SELECT InEpisodeUri,OutEpisodeUri
                FROM Episodes
                WHERE Podcastid = $id
                ";*/


        return EpisodesDict;
    }

    public override string ToString()
    {
        return $"{Id.ToString()}, {InUri}, {OutUri}";
    }
}