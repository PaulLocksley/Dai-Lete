using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;

namespace Dai_Lete.Pages;

public class Redirect(RedirectCache redirectCache) : PageModel
{
    public string? HtmlContent { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        HtmlContent = await redirectCache.GetHtmlAsync(id);
        return Page();
    }
}