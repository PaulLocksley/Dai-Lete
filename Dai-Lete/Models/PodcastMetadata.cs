namespace Dai_Lete.Models;

public struct PodcastMetadata
{
    public string title;
    public string publisher;
    public Uri? imageUrl;
    public string description;

    public PodcastMetadata(string title, string publisher, Uri? imageUrl, string description)
    {
        this.title = title;
        this.publisher = publisher;
        this.imageUrl = imageUrl;
        this.description = description;
    }
}