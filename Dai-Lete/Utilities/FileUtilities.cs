using System.Security.Cryptography;

namespace Dai_Lete.Utilities;

public class FileUtilities
{
    public static string GetMd5Sum(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = md5.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}