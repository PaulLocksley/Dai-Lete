using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Utilities;
using Dai_Lete.Services;
using Dapper;
using Microsoft.AspNetCore.Connections;

namespace Dai_Lete.Services;

public static class PodcastServices
{
    private static readonly ILogger<ConvertNewEpisodes> _logger = new Logger<ConvertNewEpisodes>(new LoggerFactory());
 
    
    public static List<Podcast> GetPodcasts()
    {
        var sql = @"Select * From Podcasts";
        var results = SqLite.Connection().Query<Podcast>(sql);
        return results.ToList();
    }

    public static (string guid, string downloadLink) GetLatestEpisode(Guid podcastId)
    {
        var RssFeed = new XmlDocument();
        var sql = @"SELECT * FROM Podcasts where id = @id";
        Podcast podcast = SqLite.Connection().QueryFirst<Podcast>(sql,new {id = podcastId});
        if (podcast is null)
        {
            throw new FileNotFoundException($"Failed to locate podcast with id {podcastId}");
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
                    continue;
                }

                string? guid = null;
                XmlNode? enclosure = null;
                String downloadLink = null;
                foreach (XmlElement n3 in n2.ChildNodes)
                {
                    if (n3.Name == "guid")
                    {
                        guid = string.Concat(n3.InnerText.Split(Path.GetInvalidFileNameChars()));
                    }

                    if (n3.Name == "enclosure") { enclosure = n3;}
                }
                foreach (XmlAttribute atr in enclosure.Attributes) {
                    if (atr.Name == "url")
                    {
                        downloadLink = atr.Value;
                        break;
                    }
                }
                if (downloadLink is null || guid is null)
                {
                    throw new Exception($"Failed to parse {podcast.InUri}");
                }
                return(guid: guid,downloadLink:downloadLink);
            }
        }
        throw new FileNotFoundException($"Could not find episode for podcast: {podcastId}");
    }

    public static void DownloadEpisode(Podcast podcast, string episodeUrl,string episodeGuid)
    {
        var localHttpClient = new HttpClient();
        localHttpClient.DefaultRequestHeaders.Add("User-Agent","Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:15.0) Gecko/20100101 Firefox/15.0.1" ); 
        
        //make folders.
        var workingDirectory = $"{Path.GetTempPath()}";
        if (!Directory.Exists($"{workingDirectory}{podcast.Id}"))
        {
            Directory.CreateDirectory($"{workingDirectory}{podcast.Id}");
        }

        workingDirectory = $"{workingDirectory}{podcast.Id}{Path.DirectorySeparatorChar}";
        var destinationLocal = ($"{workingDirectory}{episodeGuid}.local");
        try
        {
            var d1 = localHttpClient.GetByteArrayAsync(episodeUrl).ContinueWith(task =>
            {
                _logger.LogInformation("local download started");
                if (task.IsFaulted)
                {
                    Console.WriteLine($"Local download failed: {task.Exception}");
                }
                File.WriteAllBytes(destinationLocal, task.Result);
            });
                d1.Wait();
            if (d1.Status != TaskStatus.RanToCompletion)
            { 
                if(d1.Exception is not null){
                    throw d1.Exception;
                }
            }

            _logger.LogInformation("Downloads apparently done.");
        }
        catch (Exception e)
        {
            _logger.LogWarning($"Download failed of {episodeUrl}\n{e}");
            throw;
        }
    }

    public static int ProcessDownloadedEpisode(Guid id, string episodeId)
    {
       //todo reimplement.
        _logger.LogInformation($"Completed processing {episodeId}");
        return -1;
    }
    
}