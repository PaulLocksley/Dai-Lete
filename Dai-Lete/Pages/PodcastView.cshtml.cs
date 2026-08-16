using Dai_Lete.Services;
using Dai_Lete.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dai_Lete.Pages;

public class PodcastView : PageModel
{
    private readonly ConfigManager _configManager;
    private readonly FeedCacheService _feedCacheService;

    public PodcastView(ConfigManager configManager, FeedCacheService feedCacheService)
    {
        _configManager = configManager;
        _feedCacheService = feedCacheService;
    }

    [FromQuery(Name = "id")]
    public Guid? podcastID { get; set; }

    public char[] InvalidChars = Path.GetInvalidFileNameChars();
    public string BaseAddress => _configManager.GetBaseAddress();
    public PodcastMetadata PodcastMetadata { get; private set; } = new();

    public async Task OnGetAsync()
    {
        if (podcastID is null) return;

        var metadata = await _feedCacheService.GetMetaDataAsync(podcastID.Value);
        if (metadata is null)
        {
            await _feedCacheService.UpdatePodcastCacheAsync(podcastID.Value);
            metadata = await _feedCacheService.GetMetaDataAsync(podcastID.Value);
        }

        PodcastMetadata = metadata ?? new PodcastMetadata();
    }

    public void Test(string pid)
    {
        // Method appears unused - consider removing
    }
}
