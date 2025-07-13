namespace Dai_Lete.Models;

public struct PodcastMetadata
{
    public string title;
    public string publisher;
    public Uri? imageUrl;
    public string description;
    public IList<PodcastEpisodeMetadata> processedEpisodes;
    public IList<PodcastEpisodeMetadata> nonProcessedEpisodes;

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