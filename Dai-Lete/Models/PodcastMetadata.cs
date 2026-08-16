namespace Dai_Lete.Models;

public struct PodcastMetadata
{
    public string title = string.Empty;
    public string publisher = string.Empty;
    public Uri? imageUrl;
    public string description = string.Empty;
    public IList<PodcastEpisodeMetadata> processedEpisodes = new List<PodcastEpisodeMetadata>();
    public IList<PodcastEpisodeMetadata> nonProcessedEpisodes = new List<PodcastEpisodeMetadata>();

    public PodcastMetadata()
    {
    }

    public PodcastMetadata(string title, string publisher, Uri? imageUrl, string description,
        IList<PodcastEpisodeMetadata> processedEpisodes, IList<PodcastEpisodeMetadata> nonProcessedEpisodes)
    {
        this.title = title;
        this.publisher = publisher;
        this.imageUrl = imageUrl;
        this.description = description;
        this.processedEpisodes = processedEpisodes;
        this.nonProcessedEpisodes = nonProcessedEpisodes;
    }
}
