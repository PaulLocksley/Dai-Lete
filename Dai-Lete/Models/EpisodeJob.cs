namespace Dai_Lete.Models;

public class EpisodeJob
{
    public Guid PodcastId { get; set; }
    public string PodcastInUri { get; set; } = string.Empty;
    public string EpisodeUrl { get; set; } = string.Empty;
    public string EpisodeGuid { get; set; } = string.Empty;
}

public class QueuedEpisodeJob
{
    public string Id { get; set; } = string.Empty;
    public EpisodeJob Job { get; set; } = new();
}
