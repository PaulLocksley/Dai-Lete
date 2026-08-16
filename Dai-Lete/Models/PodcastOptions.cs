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

public class WorkerOptions
{
    public const string SectionName = "Worker";

    public bool Enabled { get; set; } = true;
}

public class ValkeyOptions
{
    public const string SectionName = "Valkey";

    public string ConnectionString { get; set; } = "localhost:6379";
    public string JobStream { get; set; } = "dai-lete:episode-jobs";
    public string ConsumerGroup { get; set; } = "dai-lete-workers";
    public string ConsumerName { get; set; } = Environment.MachineName;
    public string FeedCachePrefix { get; set; } = "dai-lete:feed";
    public string MetadataCachePrefix { get; set; } = "dai-lete:feed-meta";
    public int FeedCacheExpirationMinutes { get; set; } = 60;
    public int ReadBatchSize { get; set; } = 5;
}
