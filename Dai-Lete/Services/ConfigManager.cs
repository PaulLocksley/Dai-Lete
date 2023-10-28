using System.Security.Cryptography;
using System.Text;

namespace Dai_Lete.Services;

public static class ConfigManager
{
    private static readonly IConfiguration Configuration;
    public static string getBaseAddress()
    {
        var env = Environment.GetEnvironmentVariable("baseAddress");
        return env ?? "127.0.0.1";
    }

    public static string getAuthToken(string salt)
    {
        var accessToken = Environment.GetEnvironmentVariable("accessToken");
        
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            salt + ":" + (accessToken ?? "1234")));
        StringBuilder hashBuilder = new StringBuilder();
        foreach (byte b in hashBytes) { hashBuilder.Append(b.ToString("x2")); }
        return hashBuilder.ToString();
    }

    public static string getProxyAddress()
    {
        var proxyAddress = Environment.GetEnvironmentVariable("proxyAddress");
        return proxyAddress ?? "195.168.20.56:1080";
    }
}


