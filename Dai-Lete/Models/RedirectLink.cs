namespace Dai_Lete.Models;

[Serializable]
public struct RedirectLink(string originalLink, Guid? id = null)
{
    public string OriginalLink = originalLink;
    //dapper kept giving me grief,
    //something here as a guid fails to parse
    //but it works fine in the Podcast class.
    public string Id = id?.ToString().ToLowerInvariant() ?? Guid.NewGuid().ToString().ToLowerInvariant();
}