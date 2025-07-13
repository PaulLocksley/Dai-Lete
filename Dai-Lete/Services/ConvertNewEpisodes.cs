using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dapper;
using Microsoft.Extensions.Options;

namespace Dai_Lete.Services;

public class ConvertNewEpisodes : IHostedService, IDisposable
{
    private readonly ILogger<ConvertNewEpisodes> _logger;
    private readonly PodcastServices _podcastServices;
    private readonly PodcastOptions _options;
    private readonly IDatabaseService _databaseService;
    private Timer? _timer;
    private Timer? _queueTimer;
    private bool _queueLock;

    public ConvertNewEpisodes(ILogger<ConvertNewEpisodes> logger, PodcastServices podcastServices, IOptions<PodcastOptions> options, IDatabaseService databaseService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _podcastServices = podcastServices ?? throw new ArgumentNullException(nameof(podcastServices));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }


    public Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Episode conversion service starting");

        var processingInterval = TimeSpan.FromHours(_options.ProcessingIntervalHours);
        var queueCheckInterval = TimeSpan.FromSeconds(_options.QueueCheckIntervalSeconds);

        _timer = new Timer(CheckFeeds, null, TimeSpan.Zero, processingInterval);
        _queueTimer = new Timer(CheckQueue, null, TimeSpan.Zero, queueCheckInterval);

        _logger.LogInformation("Timers configured - Processing: {ProcessingInterval}, Queue: {QueueInterval}",
            processingInterval, queueCheckInterval);

        return Task.CompletedTask;
    }

    private async void CheckQueue(object? state)
    {
        if (_queueLock)
        {
            _logger.LogDebug("Queue processing already in progress");
            return;
        }

        _logger.LogDebug("Checking queue for episodes to process");
        _queueLock = true;

        try
        {
            int processedEpisodes = 0;

            while (!PodcastQueue.toProcessQueue.IsEmpty)
            {
                if (!PodcastQueue.toProcessQueue.TryDequeue(out var episodeInfo))
                {
                    break;
                }

                using var connection = await _databaseService.GetConnectionAsync();
                const string sql = "SELECT Id FROM Episodes WHERE Id = @id AND PodcastId = @pId";
                var existingEpisodes = await connection.QueryAsync<string>(sql,
                    new { pId = episodeInfo.podcast.Id, id = episodeInfo.episodeGuid });

                if (existingEpisodes.Any())
                {
                    _logger.LogInformation("Episode {EpisodeGuid} already exists, skipping", episodeInfo.episodeGuid);
                    continue;
                }

                await ProcessEpisodeAsync(episodeInfo.podcast, episodeInfo.episodeUrl, episodeInfo.episodeGuid);
                processedEpisodes++;
            }

            _logger.LogDebug("Processed {ProcessedCount} episodes from queue", processedEpisodes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing episode queue");
        }
        finally
        {
            _queueLock = false;
        }
    }
    private async void CheckFeeds(object? state)
    {
        try
        {
            _logger.LogInformation("Checking feeds for new episodes");

            var podcasts = await _podcastServices.GetPodcastsAsync();

            foreach (var podcast in podcasts)
            {
                try
                {
                    var latest = await _podcastServices.GetLatestEpisodeAsync(podcast.Id);

                    using var connection = await _databaseService.GetConnectionAsync();
                    const string sql = "SELECT Id FROM Episodes WHERE Id = @eid AND PodcastId = @pid";
                    var existingEpisodes = await connection.QueryAsync<string>(sql,
                        new { eid = latest.guid, pid = podcast.Id });

                    if (existingEpisodes.Any())
                    {
                        _logger.LogDebug("No new episodes for podcast {PodcastUri}", podcast.InUri);
                        continue;
                    }

                    _logger.LogInformation("Found new episode for podcast {PodcastUri}", podcast.InUri);
                    await ProcessEpisodeAsync(podcast, latest.downloadLink, latest.guid);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to check episodes for podcast {PodcastUri}", podcast.InUri);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking feeds for new episodes");
        }
    }

    private async Task ProcessEpisodeAsync(Podcast podcast, string downloadLink, string episodeGuid)
    {
        if (podcast is null) throw new ArgumentNullException(nameof(podcast));
        if (string.IsNullOrWhiteSpace(downloadLink)) throw new ArgumentException("Download link cannot be null or empty", nameof(downloadLink));
        if (string.IsNullOrWhiteSpace(episodeGuid)) throw new ArgumentException("Episode GUID cannot be null or empty", nameof(episodeGuid));

        _logger.LogInformation("Starting to process episode {EpisodeGuid} from {DownloadLink}", episodeGuid, downloadLink);

        try
        {
            await _podcastServices.DownloadEpisodeAsync(podcast, downloadLink, episodeGuid);
            var fileSize = await _podcastServices.ProcessDownloadedEpisodeAsync(podcast.Id, episodeGuid);

            using var connection = await _databaseService.GetConnectionAsync();
            const string sql = @"INSERT INTO Episodes (Id, PodcastId, FileSize) VALUES (@id, @pid, @fs)";
            await connection.ExecuteAsync(sql, new { id = episodeGuid, pid = podcast.Id, fs = fileSize });

            _ = FeedCache.UpdatePodcastCache(podcast.Id);

            _logger.LogInformation("Successfully processed episode {EpisodeGuid}", episodeGuid);
        }
        catch (Exception ex)
        {
            _ = FeedCache.UpdatePodcastCache(podcast.Id);
            _logger.LogError(ex, "Failed to process episode {EpisodeGuid} from {DownloadLink}", episodeGuid, downloadLink);
            throw;
        }
    }


    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Episode conversion service stopping");

        _timer?.Change(Timeout.Infinite, 0);
        _queueTimer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _queueTimer?.Dispose();
    }
}