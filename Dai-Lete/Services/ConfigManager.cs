using System.Security.Cryptography;
using System.Text;

namespace Dai_Lete.Services;

public class ConfigManager
{
    private readonly ILogger<ConfigManager> _logger;
    private readonly IConfiguration _configuration;

    public ConfigManager(ILogger<ConfigManager> logger, IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public string GetBaseAddress()
    {
        try
        {
            var baseAddress = _configuration["BaseAddress"] ?? Environment.GetEnvironmentVariable("baseAddress");
            return baseAddress ?? "127.0.0.1";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get base address");
            return "127.0.0.1";
        }
    }

    public string GetAuthToken(string salt)
    {
        if (string.IsNullOrWhiteSpace(salt))
            throw new ArgumentException("Salt cannot be null or empty", nameof(salt));

        try
        {
            var accessToken = _configuration["AccessToken"] ?? Environment.GetEnvironmentVariable("accessToken");
            var tokenValue = accessToken ?? "1234";

            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{tokenValue}"));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate auth token for salt: {Salt}", salt);
            throw;
        }
    }
}


