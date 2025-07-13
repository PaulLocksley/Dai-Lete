using System.Collections.Immutable;
using System.Data;
using System.Net;
using System.Runtime.Intrinsics.Arm;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
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
[Authorize]
public class PodcastController : Controller
{
    private readonly PodcastServices _podcastServices;
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<PodcastController> _logger;

    public PodcastController(PodcastServices podcastServices, IDatabaseService databaseService, ILogger<PodcastController> logger)
    {
        _podcastServices = podcastServices ?? throw new ArgumentNullException(nameof(podcastServices));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    [HttpPost("add")]
    public async Task<IActionResult> addPodcast(Uri inUri)
    {
        try
        {
            var p = new Podcast(inUri);

            // Validate we can read the URL as a feed
            try
            {
                using var reader = XmlReader.Create(p.InUri.ToString());
                var rssFeed = new XmlDocument();
                rssFeed.Load(reader);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse RSS feed: {Uri}", p.InUri);
                return BadRequest($"Failed to parse RSS feed: {p.InUri}");
            }

            const string sql = @"INSERT INTO Podcasts (InUri, Id) VALUES (@InUri, @Id)";
            _logger.LogInformation("Adding new podcast: {Uri} with ID: {Id}", p.InUri, p.Id);

            using var connection = await _databaseService.GetConnectionAsync();
            var rows = await connection.ExecuteAsync(sql, new { InUri = p.InUri.ToString(), Id = p.Id });

            if (rows != 1)
            {
                _logger.LogWarning("Failed to insert podcast into database, possibly duplicate: {Uri} with ID: {Id}", p.InUri, p.Id);
                return Conflict("Podcast with this ID may already exist");
            }

            _ = FeedCache.UpdatePodcastCache(p.Id);
            return Ok(p.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while adding podcast: {Uri}", inUri);
            return Problem(
                detail: "An internal server error occurred while adding podcast",
                statusCode: 500,
                title: "Internal Server Error");
        }
    }
    [HttpDelete("delete")]
    public async Task<IActionResult> deletePodcast(Guid id)
    {
        try
        {
            _logger.LogInformation("Deleting podcast {PodcastId}", id);

            const string sql = @"DELETE FROM Podcasts WHERE Id = @id";
            const string episodeSql = @"SELECT Id FROM Episodes WHERE PodcastId = @pid";

            using var connection = await _databaseService.GetConnectionAsync();
            var episodeIds = await connection.QueryAsync<string>(episodeSql, new { pid = id });

            foreach (var eId in episodeIds)
            {
                await DeletePodcastEpisode(id, eId);
            }

            _logger.LogInformation("Deleting podcast {PodcastId}", id);
            var rows = await connection.ExecuteAsync(sql, new { id = id });

            if (rows != 1)
            {
                _logger.LogWarning("Podcast not found for deletion: {PodcastId}", id);
                return NotFound("Podcast not found");
            }

            FeedCache.metaDataCache.Remove(id);
            return Ok("Podcast deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while deleting podcast: {PodcastId}", id);
            return Problem(
                detail: "An internal server error occurred while deleting podcast",
                statusCode: 500,
                title: "Internal Server Error");
        }
    }

    [HttpPost("Queue")]
    public IActionResult queueEpisode(string podcastInUri, string podcastGUID, string episodeUrl, string episodeGuid)
    {
        if (string.IsNullOrWhiteSpace(podcastGUID) || !Guid.TryParse(podcastGUID, out var parsedPodcastGuid))
        {
            _logger.LogWarning("Invalid podcast GUID provided: {PodcastGUID}", podcastGUID);
            return BadRequest("Invalid podcast GUID format");
        }

        if (!FeedCache.feedCache.ContainsKey(parsedPodcastGuid))
        {
            _logger.LogWarning("Podcast not found in cache: {PodcastId}", parsedPodcastGuid);
            return NotFound("Podcast not known to server");
        }

        if (string.IsNullOrWhiteSpace(episodeUrl) || string.IsNullOrWhiteSpace(episodeGuid))
        {
            _logger.LogWarning("Missing required parameters for episode queue");
            return BadRequest("Episode URL and GUID are required");
        }

        try
        {
            PodcastQueue.toProcessQueue.Enqueue((podcast: new Podcast(podcastGUID, podcastInUri), episodeUrl: episodeUrl, episodeGuid: episodeGuid));
            var queueCount = PodcastQueue.toProcessQueue.Count;
            _logger.LogInformation("Episode {EpisodeGuid} added to queue. Queue size: {QueueCount}", episodeGuid, queueCount);
            return Ok($"Episode added to queue. {queueCount} item/s in queue");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add episode {EpisodeGuid} to queue", episodeGuid);
            return Problem(
                detail: "An internal server error occurred while adding episode to queue",
                statusCode: 500,
                title: "Internal Server Error");
        }
    }

    [HttpDelete("DeleteEpisode")]
    public async Task<IActionResult> DeletePodcastEpisode(Guid podcastId, string episodeGuid)
    {
        if (string.IsNullOrWhiteSpace(episodeGuid))
        {
            _logger.LogWarning("Episode GUID is required for deletion");
            return BadRequest("Episode GUID is required");
        }

        if (!FeedCache.feedCache.ContainsKey(podcastId))
        {
            _logger.LogWarning("Podcast not found in cache: {PodcastId}", podcastId);
            return NotFound("Podcast not known to server");
        }

        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            const string sql = @"DELETE FROM Episodes WHERE Id = @eid AND PodcastId = @pid";
            var deletedRows = await connection.ExecuteAsync(sql, new { pid = podcastId, eid = episodeGuid });

            if (deletedRows != 1)
            {
                _logger.LogWarning("Episode not found in database: {EpisodeGuid} for podcast {PodcastId}", episodeGuid, podcastId);
                return NotFound("Episode not found in database");
            }

            var filepath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Podcasts", podcastId.ToString(), $"{episodeGuid}.wav");
            if (System.IO.File.Exists(filepath))
            {
                System.IO.File.Delete(filepath);
                _logger.LogInformation("Deleted episode file and database record: {EpisodeGuid} for podcast {PodcastId}", episodeGuid, podcastId);
            }
            else
            {
                _logger.LogWarning("Episode file not found but database record deleted: {FilePath}", filepath);
            }

            return Ok("Episode deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete episode {EpisodeGuid} for podcast {PodcastId}", episodeGuid, podcastId);
            return Problem(
                detail: "An internal server error occurred while deleting episode",
                statusCode: 500,
                title: "Internal Server Error");
        }
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
    [AllowAnonymous]
    public async Task<IActionResult> getFeed(Guid id)
    {
        try
        {
            if (!FeedCache.feedCache.ContainsKey(id))
            {
                _logger.LogWarning("Podcast feed not found in cache: {PodcastId}", id);
                return NotFound($"Podcast feed not found for ID: {id}");
            }

            var feedXml = FeedCache.feedCache[id].OuterXml;
            _logger.LogDebug("Serving podcast feed for {PodcastId}", id);
            return Content(feedXml, "application/xml");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve podcast feed for {PodcastId}", id);
            return Problem(
                detail: "An internal server error occurred while retrieving podcast feed",
                statusCode: 500,
                title: "Internal Server Error");
        }
    }

}