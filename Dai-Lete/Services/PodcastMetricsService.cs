using System.Diagnostics.Metrics;
using System.Diagnostics;
using Dai_Lete.Models;

namespace Dai_Lete.Services;

public class PodcastMetricsService
{
    private static readonly Meter Meter = new("Dai_Lete.Podcast");
    private static readonly Counter<double> TimeSavedCounter = Meter.CreateCounter<double>(
        "podcast_time_saved_seconds_total",
        "seconds",
        "Total time saved by removing ads from podcasts");

    private static readonly Histogram<double> TimeSavedHistogram = Meter.CreateHistogram<double>(
        "podcast_time_saved_seconds",
        "seconds",
        "Time saved per episode by removing ads");

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

        var tags = new TagList
        {
            { "podcast_id", podcastId.ToString() },
            { "episode_id", episodeId }
        };

        if (!string.IsNullOrEmpty(podcastName))
        {
            tags.Add("podcast_name", podcastName);
        }

        TimeSavedCounter.Add(timeSavedSeconds, tags);
        TimeSavedHistogram.Record(timeSavedSeconds, tags);

        _logger.LogInformation("Recorded {TimeSaved:F1} seconds saved for podcast {PodcastId}, episode {EpisodeId}",
            timeSavedSeconds, podcastId, episodeId);
    }
}