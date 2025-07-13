namespace Dai_Lete.Models;

[Serializable]
public struct RedirectLink(string OriginalLink, Guid Id)
{
    public string OriginalLink = OriginalLink;
    //dapper kept giving me grief,
    //something here as a guid fails to parse
    //but it works fine in the Podcast class.
    public string Id = Id.ToString().ToLowerInvariant();
}