using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dai_Lete.Pages;

public class PodcastView : PageModel
{
    [FromQuery(Name = "id")]
    public Guid? podcastID { get; set; }

    public char[] InvalidChars = Path.GetInvalidFileNameChars();
    
    public void OnGet()
    {

    }

    public void Test(string pid)
    {
        Console.WriteLine(pid);
    }
}