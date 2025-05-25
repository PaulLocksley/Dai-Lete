using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dapper;

namespace Dai_Lete.Services;

public class ConvertNewEpisodes : IHostedService, IDisposable
{
    private readonly ILogger<ConvertNewEpisodes> _logger;
    private Timer? _timer = null;
    private Timer? _QueueTimer = null;
    private bool _QueueLock = false;
    public ConvertNewEpisodes(ILogger<ConvertNewEpisodes> logger)
    {
        _logger = logger;
    }
    
    
    public Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Timed Hosted Service running.");

        _timer = new Timer(CheckFeeds, null, TimeSpan.Zero,
            TimeSpan.FromHours(1));
        _QueueTimer = new Timer(CheckQueue, null, TimeSpan.Zero, TimeSpan.FromSeconds(10)); 
        return Task.CompletedTask;
    }

    private void CheckQueue(Object? state)
    {
        if (_QueueLock)
        {
            _logger.LogInformation("Queued work in progress but service attempted to check for latest episodes");
            return;
        }
        _logger.LogDebug("Checking Queue for processing episodes.");
        _QueueLock = true;
        try
        {
            int processedEps = 0;
            while (!PodcastQueue.toProcessQueue.IsEmpty)
            {
                //(Podcast podcast, string episodeUrl,string episodeGuid) episodeInfo;
                var deQueueResult = PodcastQueue.toProcessQueue.TryDequeue(out var episodeInfo);
                if (!deQueueResult) { return; }

                var sql = "Select Id FROM Episodes WHERE Id = @id AND PodcastId = @pId";
                var isPresentList = SqLite.Connection().Query<string>(sql,
                    new { pId = episodeInfo.podcast.Id, id = episodeInfo.episodeGuid });
                if (isPresentList.Any())
                {
                    _logger.LogInformation($"Episode {episodeInfo.episodeGuid} found in database, skipping");
                    continue;
                }

                processEpisode(episodeInfo.podcast, episodeInfo.episodeUrl, episodeInfo.episodeGuid);
                processedEps++;
            }
            _QueueLock = false;
            if(processedEps > 0){}
            _logger.LogDebug($"Queue processed {processedEps} episodes");
        }
        catch (Exception e)
        {
            _QueueLock = false;
            throw;
        }
    }
    private void CheckFeeds(object? state)
    {
        var plist = PodcastServices.GetPodcasts();
        foreach (var podcast in plist)
        {
            try
            {
                var sql = "Select Id FROM Episodes WHERE Id = @eid AND PodcastId = @pid";
                var latest = PodcastServices.GetLatestEpsiode(podcast.Id);
                var episodeList = SqLite.Connection().Query<string>(sql, new { eid = latest.guid, pid = podcast.Id });

                if (episodeList.Any())
                {
                    _logger.LogInformation($"No new episodes of {podcast.InUri}");
                    continue;
                }

                _logger.LogInformation($"New episodes of {podcast.InUri}");
                processEpisode(podcast, latest.downloadLink, latest.guid);
            }
            catch (Exception e)
            {
                _logger.LogError($"Failed to parse episode of {podcast.InUri}");
                _logger.LogError(e.Source);
                _logger.LogError(e.Message);
                _logger.LogError(e.StackTrace);
            }
        }
       
    }

    private void processEpisode(Podcast podcast, string DownloadLink, string episodeGuid)
    {
        _logger.LogInformation($"Starting to process episode: {DownloadLink}");
        try
        {
            //episodeGuid = string.Concat(episodeGuid.Split(Path.GetInvalidFileNameChars()));
            PodcastServices.DownloadEpisode(podcast, DownloadLink, episodeGuid);
            var filesize = PodcastServices.ProcessDownloadedEpisode(podcast.Id, episodeGuid);
            var sql = @"INSERT INTO Episodes (Id,PodcastId,FileSize) VALUES (@id,@pid,@fs)";
            SqLite.Connection().Execute(sql, new { id = episodeGuid, pid = podcast.Id, fs = filesize });
            //schedule a new copy of the item.
            _ = FeedCache.UpdatePodcastCache(podcast.Id);
            _logger.LogInformation($"Completed processing episode: {DownloadLink}");
        }
        catch (Exception e)
        {
            _ = FeedCache.UpdatePodcastCache(podcast.Id);
            _logger.LogError($"Episode Processing Failed {e}");
        }

    }
    

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Timed Hosted Service is stopping.");

        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}