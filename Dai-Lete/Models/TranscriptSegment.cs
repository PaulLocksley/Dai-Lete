namespace Dai_Lete.Models;

public class TranscriptSegment
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
}

public class EpisodeTranscript
{
    public string EpisodeId { get; set; } = string.Empty;
    public string PodcastId { get; set; } = string.Empty;
    public List<TranscriptSegment> Segments { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Language { get; set; } = "en";
}