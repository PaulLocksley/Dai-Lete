

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

    public int GetLookAheadDistance()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("lookAheadDistance"), out var envValue))
            return envValue;
        if (int.TryParse(_configuration["lookAheadDistance"], out var configValue))
            return configValue;
        return 60 * 8;
    }
    public string GetUsername()
    {
        try
        {
            return Environment.GetEnvironmentVariable("AUTH_USERNAME") ?? _configuration["Auth:Username"] ?? "admin";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get username");
            return "admin";
        }
    }

    public string GetPassword()
    {
        try
        {
            return Environment.GetEnvironmentVariable("AUTH_PASSWORD") ?? _configuration["Auth:Password"] ?? "password";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get password");
            return "password";
        }
    }

    public string GetProxyAddress()
    {
        try
        {
            var proxyAddress = _configuration["ProxyAddress"] ?? Environment.GetEnvironmentVariable("proxyAddress");
            return proxyAddress ?? throw new InvalidOperationException("Proxy address not configured");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get proxy address");
            throw;
        }
    }

    public string GetPodcastStoragePath()
    {
        try
        {
            var storagePath = _configuration["PodcastStoragePath"] ?? Environment.GetEnvironmentVariable("podcastStoragePath");
            return storagePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Podcasts");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get podcast storage path, using default");
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Podcasts");
        }
    }
}


