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
    private readonly IEpisodeJobQueue _episodeJobQueue;
    private readonly ConfigManager _configManager;
    private readonly ILogger<PodcastController> _logger;

    public PodcastController(PodcastServices podcastServices, IDatabaseService databaseService, IEpisodeJobQueue episodeJobQueue, ConfigManager configManager, ILogger<PodcastController> logger)
    {
        _podcastServices = podcastServices ?? throw new ArgumentNullException(nameof(podcastServices));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _episodeJobQueue = episodeJobQueue ?? throw new ArgumentNullException(nameof(episodeJobQueue));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
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
                await DeleteEpisodeInternal(connection, id, eId);
            }

            _logger.LogInformation("Deleting podcast {PodcastId}", id);
            var rows = await connection.ExecuteAsync(sql, new { id = id });

            if (rows != 1)
            {
                _logger.LogWarning("Podcast not found for deletion: {PodcastId}", id);
                return NotFound("Podcast not found");
            }

            await FeedCache.RemovePodcastCacheAsync(id);
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
    public async Task<IActionResult> queueEpisode(string podcastInUri, string podcastGUID, string episodeUrl, string episodeGuid)
    {
        if (string.IsNullOrWhiteSpace(podcastGUID) || !Guid.TryParse(podcastGUID, out var parsedPodcastGuid))
        {
            _logger.LogWarning("Invalid podcast GUID provided: {PodcastGUID}", podcastGUID);
            return BadRequest("Invalid podcast GUID format");
        }

        if (string.IsNullOrWhiteSpace(episodeUrl) || string.IsNullOrWhiteSpace(episodeGuid))
        {
            _logger.LogWarning("Missing required parameters for episode queue");
            return BadRequest("Episode URL and GUID are required");
        }

        try
        {
            using var connection = await _databaseService.GetConnectionAsync();
            const string podcastSql = "SELECT Id FROM Podcasts WHERE Id = @id";
            var knownPodcast = await connection.QueryFirstOrDefaultAsync<string?>(podcastSql, new { id = parsedPodcastGuid });
            if (knownPodcast is null)
            {
                _logger.LogWarning("Podcast not found: {PodcastId}", parsedPodcastGuid);
                return NotFound("Podcast not known to server");
            }

            var jobId = await _episodeJobQueue.EnqueueAsync(new EpisodeJob
            {
                PodcastId = parsedPodcastGuid,
                PodcastInUri = podcastInUri,
                EpisodeUrl = episodeUrl,
                EpisodeGuid = episodeGuid
            });

            if (string.IsNullOrEmpty(jobId))
            {
                return Ok("Episode already queued");
            }

            _logger.LogInformation("Episode {EpisodeGuid} added to Valkey queue as job {JobId}", episodeGuid, jobId);
            return Ok($"Episode added to queue. Job ID: {jobId}");
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

    private async Task<bool> DeleteEpisodeInternal(IDbConnection connection, Guid podcastId, string episodeGuid)
    {
        if (string.IsNullOrWhiteSpace(episodeGuid))
        {
            _logger.LogWarning("Episode GUID is required for deletion");
            return false;
        }

        try
        {
            const string sql = @"DELETE FROM Episodes WHERE Id = @eid AND PodcastId = @pid";
            var deletedRows = await connection.ExecuteAsync(sql, new { pid = podcastId, eid = episodeGuid });

            if (deletedRows != 1)
            {
                _logger.LogWarning("Episode not found in database: {EpisodeGuid} for podcast {PodcastId}", episodeGuid, podcastId);
                return false;
            }

            var filepath = Path.Combine(_configManager.GetPodcastStoragePath(), podcastId.ToString(), $"{episodeGuid}.mp3");
            if (System.IO.File.Exists(filepath))
            {
                System.IO.File.Delete(filepath);
                _logger.LogInformation("Deleted episode file and database record: {EpisodeGuid} for podcast {PodcastId}", episodeGuid, podcastId);
            }
            else
            {
                _logger.LogWarning("Episode file not found but database record deleted: {FilePath}", filepath);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete episode {EpisodeGuid} for podcast {PodcastId}", episodeGuid, podcastId);
            return false;
        }
    }

    private async Task<bool> DeleteEpisodeInternal(Guid podcastId, string episodeGuid)
    {
        using var connection = await _databaseService.GetConnectionAsync();
        return await DeleteEpisodeInternal(connection, podcastId, episodeGuid);
    }

    [HttpDelete("DeleteEpisode")]
    public async Task<IActionResult> DeletePodcastEpisode(Guid podcastId, string episodeGuid)
    {
        if (string.IsNullOrWhiteSpace(episodeGuid))
        {
            _logger.LogWarning("Episode GUID is required for deletion");
            return BadRequest("Episode GUID is required");
        }

        var success = await DeleteEpisodeInternal(podcastId, episodeGuid);
        
        if (!success)
        {
            return NotFound("Episode not found in database");
        }

        await FeedCache.UpdatePodcastCache(podcastId);
        return Ok("Episode deleted successfully");
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
            var feedXml = await FeedCache.GetPodcastFeedXmlAsync(id);
            if (feedXml is null)
            {
                _logger.LogInformation("Podcast feed not found in Valkey cache, rebuilding: {PodcastId}", id);
                await FeedCache.UpdatePodcastCache(id);
                feedXml = await FeedCache.GetPodcastFeedXmlAsync(id);
            }

            if (feedXml is null)
            {
                _logger.LogWarning("Podcast feed not found after rebuild: {PodcastId}", id);
                return NotFound($"Podcast feed not found for ID: {id}");
            }

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
