namespace Dai_Lete.Models;

public class PodcastEpisodeMetadata
{
    public string episodeName;
    public string description;
    public DateTime? pubDate;
    public Uri? imageLink;
    public Uri? downloadLink;
    public string episodeId;
    public Podcast Podcast;
    public double? initialLengthSeconds;
    public double? processedLengthSeconds;

    public PodcastEpisodeMetadata()
    {
        this.episodeName = string.Empty;
        this.description = string.Empty;
        this.pubDate = null;
        this.imageLink = null;
        this.downloadLink = null;
        this.episodeId = string.Empty;
        this.Podcast = new Podcast();
        this.initialLengthSeconds = null;
        this.processedLengthSeconds = null;
    }

    public PodcastEpisodeMetadata(string episodeName, string description, DateTime? pubDate, Uri? imageLink
        , string episodeId, Podcast podcast)
    {
        this.episodeName = episodeName;
        this.description = description;
        this.pubDate = pubDate;
        this.imageLink = imageLink;
        this.episodeId = episodeId;
        this.Podcast = podcast;
        this.initialLengthSeconds = null;
        this.processedLengthSeconds = null;
    }

    public PodcastEpisodeMetadata(Podcast podcast)
    {
        this.episodeName = string.Empty;
        this.description = string.Empty;
        this.pubDate = null;
        this.imageLink = null;
        this.episodeId = string.Empty;
        this.Podcast = podcast;
        this.initialLengthSeconds = null;
        this.processedLengthSeconds = null;
    }
}
