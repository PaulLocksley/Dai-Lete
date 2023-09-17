using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dai_Lete.Pages;

public class PodcastView : PageModel
{
    [FromQuery(Name = "id")]
    public Guid? podcastID { get; set; }

    
    public void OnGet()
    {
        
    }
}