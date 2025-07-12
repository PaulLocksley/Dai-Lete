namespace Dai_Lete.Models;

public class PodcastSettings(IList<string> userPrompts, int maxEpisodes = 3, bool autoDownload = true)
{
    public IList<string> UserPrompts = userPrompts;
    public int MaxEpisodes = maxEpisodes;
    public bool AutoDownload = autoDownload;
}