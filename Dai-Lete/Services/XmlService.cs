using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dapper;

namespace Dai_Lete.Services;

public class XmlService
{
    public static XmlDocument GenerateNewFeed(Guid podcastId)
    {
        //Generate a new XML feed for the podcast, generating the feed will also refresh the metadata cache for this pod
        var RssFeed = new XmlDocument();
        
        string metaDataName = String.Empty;
        Uri? metaDataImageUrl = null;
        string metaDataAuthor = String.Empty;
        string metaDataDescription = String.Empty;
        IList<PodcastEpisodeMetadata> processedEpisodes = new List<PodcastEpisodeMetadata>();
        IList<PodcastEpisodeMetadata> nonProcessedEpisodes = new List<PodcastEpisodeMetadata>();
        
        var sql = @"SELECT * FROM Podcasts where id = @podcastId";
        Podcast podcast = SqLite.Connection().QueryFirst<Podcast>(sql,new { podcastId = podcastId});
        
        sql = @"SELECT Id,FileSize FROM Episodes where PodcastId = @pid";
        var episodes = SqLite.Connection().Query(sql, new { pid = podcastId }).ToDictionary(
                row=> (string)row.Id, 
                row => (int)row.FileSize
                );
        if (podcast is null)
        {
            throw new FileNotFoundException($"Failed to locate podcast with podcastId {podcastId}");
        }
        try
        {
            using (var reader = XmlReader.Create(podcast.InUri.ToString()))
            {
                RssFeed.Load(reader);
            }
        }
        catch
        {
            throw new Exception($"Failed to parse {podcast.InUri}");
        }

        var root = RssFeed.DocumentElement;
        
        foreach (XmlElement node in root.ChildNodes)
        {
            foreach (XmlElement n2 in node.ChildNodes)
            {
                if (n2.Name != "item")
                {
                    //build metadata.
                    switch (n2.Name)
                    {
                        case "title":
                            metaDataName = n2.InnerText;
                            break;
                        case "description":
                            metaDataDescription = n2.InnerText;
                            break;
                        case "itunes:author":
                            metaDataAuthor = n2.InnerText;
                            break;
                        case "itunes:image":
                            metaDataImageUrl = new Uri(n2.Attributes.GetNamedItem("href").InnerText);
                            break;
                    }
                    continue;
                }

                string? guid = null;
                XmlNode? enclosure = null;
                foreach (XmlElement n3 in n2.ChildNodes)
                {
                    if (n3.Name == "guid") { guid = String.Concat(n3.InnerText.Split(Path.GetInvalidFileNameChars()));}//todo: Set this globally.
                    if (n3.Name == "enclosure") { enclosure = n3;}
                }

                if(episodes.ContainsKey(guid))
                {
                    processedEpisodes.Add(GetEpisodeMetaData(n2,podcast));
                    foreach (XmlAttribute atr in enclosure.Attributes)
                    {
                        switch (atr.Name)
                        {
                            case "url":
                                atr.Value = $"https://{ConfigManager.getBaseAddress()}/Podcasts/{podcastId}/{guid}.mp3";
                                break;
                            case "length":
                                atr.Value = episodes[guid].ToString();
                                break;
                            case "type":
                                atr.Value = "audio/mpeg";
                                break;
                        }
                    }
                }
                else
                {
                    nonProcessedEpisodes.Add(GetEpisodeMetaData(n2,podcast));
                }
            }
        }

        //metadata area.
        FeedCache.updateMetaData(podcastId, new PodcastMetadata(metaDataName, metaDataAuthor,
                                                                metaDataImageUrl, metaDataDescription,
                                                                processedEpisodes,nonProcessedEpisodes));
        return RssFeed;
    }


    private static PodcastEpisodeMetadata GetEpisodeMetaData(XmlElement podcastEpisode, Podcast podcast)
    {
        var pm = new PodcastEpisodeMetadata(podcast);
        foreach (XmlElement node in podcastEpisode.ChildNodes)
        {
            switch (node.Name)
            {
                case "title":
                    pm.episodeName = node.InnerText;
                    break;
                case "description":
                    pm.description = node.InnerText;
                    break;
                case "guid":
                    pm.episodeId = string.Concat(node.InnerText.Split(Path.GetInvalidFileNameChars()));
                    break;
                case "pubDate":
                    pm.pubDate = DateTime.Parse(node.InnerText);
                    break;
                case "itunes:image":
                    //todo fix uri parsing elsewhere.
                    if(Uri.TryCreate(node.Attributes.GetNamedItem("href")?.InnerText, UriKind.Absolute, out var outUri))
                    {
                        pm.imageLink = outUri;
                    }
                    break;
                case "enclosure":
                    if(Uri.TryCreate(node.Attributes.GetNamedItem("url")?.InnerText, UriKind.Absolute, out var outUri2))
                    {
                        pm.downloadLink = outUri2;
                    }
                    break;
            }
        }
        return pm;
    }
}