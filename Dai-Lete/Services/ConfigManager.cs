namespace Dai_Lete.Services;

public static class ConfigManager
{
    private static readonly IConfiguration Configuration;
    public static string getBaseAddress()
    {
        var env = Environment.GetEnvironmentVariable("baseAddress");
        return env ?? "127.0.0.1";
    }
}