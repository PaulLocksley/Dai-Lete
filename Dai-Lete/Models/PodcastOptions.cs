namespace Dai_Lete.Models;

public class PodcastOptions
{
    public const string SectionName = "Podcast";

    public string StoragePath { get; set; } = "Podcasts";
    public int FeedCacheExpirationHours { get; set; } = 1;
    public int ProcessingIntervalHours { get; set; } = 1;
    public int QueueCheckIntervalSeconds { get; set; } = 10;
}

public class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Path { get; set; } = "Podcasts/Podcasts.sqlite";
}