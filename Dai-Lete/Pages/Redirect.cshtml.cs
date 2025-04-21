using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dai_Lete.Pages;

public class Redirect(IHttpClientFactory httpClientFactory) : PageModel
{
    public string? HtmlContent { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var record = RedirectService.GetRedirectLink(id); 
        var client = httpClientFactory.CreateClient();
        HtmlContent = await client.GetStringAsync(record.OriginalLink);

        return Page();
    }
}