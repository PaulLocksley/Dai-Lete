namespace Dai_Lete.Services;

public static class ConfigManager
{
    private static readonly IConfiguration Configuration;
    public static string getBaseAddress()
    {
        var env = Environment.GetEnvironmentVariable("baseAddress");
        return env ?? "127.0.0.1";
    }

    public static string getAuthToken()
    {
        var accessToken = Environment.GetEnvironmentVariable("accessToken");
        return accessToken ?? "1234";
    }

    public static string getProxyAddress()
    {
        var proxyAddress = Environment.GetEnvironmentVariable("proxyAddress");
        return proxyAddress ?? "192.168.20.56:1080";
    }
}