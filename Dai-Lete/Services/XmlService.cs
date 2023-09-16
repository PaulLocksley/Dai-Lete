using System.Net;
using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Dapper;

namespace Dai_Lete.ScheduledTasks;

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
        foreach (XmlElement node in RssFeed.DocumentElement.ChildNodes)
        {
            foreach (XmlElement n2 in node.ChildNodes)
            {
                if (n2.Name != "item")
                {
                    //build metadata.
                    var items = new string[] {"title","itunes:author","description"};//,"itunes:image"};
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

                XmlNode? guid = null;
                XmlNode? enclosure = null;
                foreach (XmlElement n3 in n2.ChildNodes)
                {
                    if (n3.Name == "guid") { guid = n3;}
                    if (n3.Name == "enclosure") { enclosure = n3;}
                }

                if(episodes.ContainsKey(guid.InnerText))
                {
                    foreach (XmlAttribute atr in enclosure.Attributes)
                    {
                        switch (atr.Name)
                        {
                            case "url":
                                atr.Value = $"https://{ConfigManager.getBaseAddress()}/Podcasts/{podcastId}/{guid.InnerText}.mp3";
                                break;
                            case "length":
                                atr.Value = episodes[guid.InnerText].ToString();
                                break;
                            case "type":
                                atr.Value = "audio/mpeg";
                                break;
                        }
                    }
                }
            }
        }
        
        //metadata area.
        FeedCache.updateMetaData(podcastId, new PodcastMetadata(metaDataName, metaDataAuthor,
                                                                metaDataImageUrl, metaDataDescription));
        
        return RssFeed;
    }
}