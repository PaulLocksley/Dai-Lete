using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;

namespace Dai_Lete.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly ConfigManager _configManager;
    private readonly FeedCacheService _feedCacheService;

    public IndexModel(ILogger<IndexModel> logger, ConfigManager configManager, FeedCacheService feedCacheService)
    {
        _logger = logger;
        _configManager = configManager;
        _feedCacheService = feedCacheService;
    }

    public object InUri { get; set; } = string.Empty;
    public string BaseAddress => _configManager.GetBaseAddress();
    public IReadOnlyDictionary<Guid, PodcastMetadata> PodcastMetadata { get; private set; } = new Dictionary<Guid, PodcastMetadata>();

    public async Task OnGetAsync()
    {
        PodcastMetadata = await _feedCacheService.GetAllMetaDataAsync();
    }

}
