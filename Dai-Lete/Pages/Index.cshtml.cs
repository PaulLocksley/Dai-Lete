using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
namespace Dai_Lete.Pages;

public class IndexModel : PageModel
{

    private readonly ILogger<IndexModel> _logger;
    public IndexModel(ILogger<IndexModel> logger)
    {
        _logger = logger;
    }

    public object InUri { get; set; }

    public void OnGet()
    {
    }
    
}