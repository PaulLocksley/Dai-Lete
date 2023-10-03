using System.Data;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.ScheduledTasks;
using Dai_Lete.Services;
using Dapper;

namespace Dai_Lete.Controllers;
[ApiController]
[Route("[controller]")]
public class PodcastController : Controller
{
    [HttpPost("add")]
    public Guid addPodcast(Uri inUri, string authToken)
    {
        var enc = Encoding.UTF8;
        if (SHA256.HashData(enc.GetBytes(inUri + ":" + ConfigManager.getAuthToken())).ToString() != authToken)
        {
            throw new AuthenticationException("Not authorised");
        }

        var p = new Podcast(inUri);
        var sql = @"INSERT INTO Podcasts (inuri,id)  VALUES (@InUri,@Id)";
        Console.WriteLine($"obj: {p.InUri.ToString()} ,{p.Id}");
        var rows = SqLite.Connection().Execute(sql, new { InUri = p.InUri.ToString(), Id = p.Id });
        if (rows != 1)
        {
            throw new DataException();
        }

        return p.Id;
    }

    [HttpPost("Queue")]
    public string queueEpisode(string podcastInUri, string podcastGUID, string episodeUrl, string episodeGuid)
    {
        //todo: changed 
        if (Guid.TryParse(podcastGUID, out var  parsedPodcastGuid) && !FeedCache.feedCache.ContainsKey(parsedPodcastGuid))
        {
            throw new Exception("Error, could not parse podcast Guid or podcast not known to server.");
        }
        PodcastQueue.toProcessQueue.Enqueue((Podcast:new Podcast(podcastGUID,podcastInUri),episodeUrl: episodeUrl,episodeGuid: episodeGuid));
        return $"Episode added to queue. {PodcastQueue.toProcessQueue.Count} item/s in queue ";
    }

    [HttpDelete("DeleteEpisode")]
    public bool DeletePodcastEpisode(Guid podcastId, string episodeGuid)
    {
        if(!FeedCache.feedCache.ContainsKey(podcastId))
        {
            throw new Exception("Error, podcast not known to server.");
        }

        var sql = @"DELETE FROM Episodes WHERE id==@eid and podcastid==@pid";
        var i = SqLite.Connection().Execute(sql, new { pid = podcastId, eid = episodeGuid });
        if (i != 1)
        {
            throw new Exception("Error, you dun messed up...");
        }
        var filepath = $"{AppDomain.CurrentDomain.BaseDirectory}Podcasts{Path.DirectorySeparatorChar}{podcastId}{Path.DirectorySeparatorChar}{episodeGuid}.mp3";
        if (!System.IO.File.Exists(filepath))
        {
            throw new Exception("Could not find actual file.");
        }
        System.IO.File.Delete(filepath);
        Console.WriteLine($"Deleted episode {episodeGuid} of podcast {podcastId}");//todo:add logger to controller
        return true;
    }
    
    [HttpGet("list-podcasts")]
    [Produces("application/json", "application/xml")]
    public IActionResult listPodcasts()
    {
        return Ok(PodcastServices.GetPodcasts().Select(x => x.ToString())); //todo: Work out how to setup default serialization because this is stupid.
    }

    [HttpGet("podcast-feed")]
    [Produces("application/xml")]
    public ContentResult getFeed(Guid id)
    {
        if (!FeedCache.feedCache.ContainsKey(id))
        {
            throw new Exception($"Failed to find podcast with key: {id}");
        }
        return Content(FeedCache.feedCache[id].OuterXml, "application/xml");
        //return Content(XmlService.GenerateNewFeed(id).OuterXml, "application/xml");
    }
    
}