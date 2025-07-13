using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using Dai_Lete.Services;

namespace Dai_Lete.Pages.Auth;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly ConfigManager _configManager;
    private readonly ILogger<LoginModel> _logger;
    private readonly IMemoryCache _cache;

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public LoginModel(ConfigManager configManager, ILogger<LoginModel> logger, IMemoryCache cache)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var rateLimitKey = $"login_attempts_{clientIp}";

        if (_cache.TryGetValue(rateLimitKey, out int attempts) && attempts >= 5)
        {
            _logger.LogWarning("Rate limit exceeded for IP: {ClientIp}", clientIp);
            ModelState.AddModelError(string.Empty, "Too many login attempts. Please try again later.");
            return Page();
        }

        var configuredUsername = _configManager.GetUsername();
        var configuredPassword = _configManager.GetPassword();

        if (Username == configuredUsername && Password == configuredPassword)
        {
            _cache.Remove(rateLimitKey);
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, Username),
                new(ClaimTypes.NameIdentifier, Username)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity), authProperties);

            _logger.LogInformation("User {Username} logged in successfully", Username);

            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return Redirect(ReturnUrl);
            }

            return RedirectToPage("/Index");
        }

        var newAttempts = attempts + 1;
        _cache.Set(rateLimitKey, newAttempts, TimeSpan.FromMinutes(15));

        _logger.LogWarning("Failed login attempt for username: {Username} from IP: {ClientIp} (attempt {Attempts})",
            Username, clientIp, newAttempts);
        ModelState.AddModelError(string.Empty, "Invalid username or password");
        return Page();
    }
}