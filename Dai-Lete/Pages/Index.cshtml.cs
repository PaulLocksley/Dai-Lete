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

    public IndexModel(ILogger<IndexModel> logger, ConfigManager configManager)
    {
        _logger = logger;
        _configManager = configManager;
    }

    public object InUri { get; set; }
    public string BaseAddress => _configManager.GetBaseAddress();

    public void OnGet()
    {
    }

}