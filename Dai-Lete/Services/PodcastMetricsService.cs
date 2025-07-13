using Prometheus;

namespace Dai_Lete.Services;

public class PodcastMetricsService
{
    private static readonly Counter TimeSavedCounter = Metrics
        .CreateCounter("podcast_time_saved_seconds_total", "Total time saved by removing ads from podcasts",
            new[] { "podcast_id", "episode_id", "podcast_name" });

    private static readonly Histogram TimeSavedHistogram = Metrics
        .CreateHistogram("podcast_time_saved_seconds", "Time saved per episode by removing ads",
            new[] { "podcast_id", "episode_id", "podcast_name" });

    private readonly ILogger<PodcastMetricsService> _logger;

    public PodcastMetricsService(ILogger<PodcastMetricsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RecordTimeSaved(Guid podcastId, string episodeId, TimeSpan timeSaved, string? podcastName = null)
    {
        var timeSavedSeconds = timeSaved.TotalSeconds;

        if (timeSavedSeconds < 0)
        {
            _logger.LogWarning("Negative time saved recorded for podcast {PodcastId}, episode {EpisodeId}: {TimeSaved}s",
                podcastId, episodeId, timeSavedSeconds);
            return;
        }

        var labels = new[] { podcastId.ToString(), episodeId, podcastName ?? "" };

        TimeSavedCounter.WithLabels(labels).Inc(timeSavedSeconds);
        TimeSavedHistogram.WithLabels(labels).Observe(timeSavedSeconds);

        _logger.LogInformation("Recorded {TimeSaved:F1} seconds saved for podcast {PodcastId}, episode {EpisodeId}",
            timeSavedSeconds, podcastId, episodeId);
    }
}