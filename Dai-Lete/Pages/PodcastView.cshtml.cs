using Dai_Lete.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dai_Lete.Pages;

public class PodcastView : PageModel
{
    private readonly ConfigManager _configManager;

    public PodcastView(ConfigManager configManager)
    {
        _configManager = configManager;
    }

    [FromQuery(Name = "id")]
    public Guid? podcastID { get; set; }

    public char[] InvalidChars = Path.GetInvalidFileNameChars();
    public string BaseAddress => _configManager.GetBaseAddress();
    
    public void OnGet()
    {

    }

    public void Test(string pid)
    {
        // Method appears unused - consider removing
    }
}