using System.Collections.Immutable;
using System.Data;
using System.Net;
using System.Runtime.Intrinsics.Arm;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Dapper;

namespace Dai_Lete.Controllers;

[ApiController]
[Route("[controller]")]
public class PodcastController : Controller
{
    private readonly ConfigManager _configManager;
    private readonly PodcastServices _podcastServices;
    private readonly ILogger<PodcastController> _logger;

    public PodcastController(ConfigManager configManager, PodcastServices podcastServices, ILogger<PodcastController> logger)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _podcastServices = podcastServices ?? throw new ArgumentNullException(nameof(podcastServices));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    [HttpPost("add")]
    public IActionResult addPodcast(Uri inUri, string authToken)
    {
        if (_configManager.GetAuthToken(inUri.ToString()) != authToken)
        {
            Console.WriteLine($"{authToken} did not match expected value");
            return StatusCode(401);
        }
        var p = new Podcast(inUri);
        //validate we can read the url as a feed.
        try
        {
            using var reader = XmlReader.Create(p.InUri.ToString());
            var RssFeed = new XmlDocument();
            RssFeed.Load(reader);
        }
        catch
        {
            return StatusCode(400, $"Failed to parse {p.InUri}");
        }
        
        
        var sql = @"INSERT INTO Podcasts (inuri,id)  VALUES (@InUri,@Id)";
        Console.WriteLine($"obj: {p.InUri.ToString()} ,{p.Id}");
        var rows = SqLite.Connection().Execute(sql, new { InUri = p.InUri.ToString(), Id = p.Id });
        if (rows != 1)
        {
            throw new DataException();
        }
        
        _ = FeedCache.UpdatePodcastCache(p.Id);
        return StatusCode(200,p.Id);
    }
    [HttpDelete("delete")]
    public IActionResult deletePodcast(Guid id, string authToken)
    {
        if (_configManager.GetAuthToken(id.ToString()) != authToken)
        {
            Console.WriteLine($"{authToken} did not match expected value");
            return StatusCode(401);
        }
        Console.WriteLine($"AuthToken accepted, deleting podcast {id}");
        var sql = @"DELETE FROM Podcasts WHERE @id = Id";
        var episodeSql = @"SELECT Id FROM Episodes where PodcastId=@pid";
        var episodeIds = SqLite.Connection().Query<string>(episodeSql, new {pid = id});
        foreach (var eId in episodeIds)
        {
            DeletePodcastEpisode(id, eId);
        }
        Console.WriteLine($"Deleting podcast {id}");
        var rows = SqLite.Connection().Execute(sql, new { id = id });
        if (rows != 1)
        {
            throw new DataException();
        }
        FeedCache.metaDataCache.Remove(id);
        return StatusCode(200);
    }

    [HttpPost("Queue")]
    public string queueEpisode(string podcastInUri, string podcastGUID, string episodeUrl, string episodeGuid)
    {
        //todo: changed 
        if (Guid.TryParse(podcastGUID, out var  parsedPodcastGuid) && !FeedCache.feedCache.ContainsKey(parsedPodcastGuid))
        {
            throw new Exception("Error, could not parse podcast Guid or podcast not known to server.");
        }
        PodcastQueue.toProcessQueue.Enqueue((podcast: new Podcast(podcastGUID, podcastInUri), episodeUrl: episodeUrl, episodeGuid: episodeGuid));
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
    public async Task<IActionResult> listPodcasts()
    {
        var podcasts = await _podcastServices.GetPodcastsAsync();
        return Ok(podcasts.Select(x => x.ToString()));
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