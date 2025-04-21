using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dapper;

namespace Dai_Lete.Services;

public class RedirectService
{
    private static readonly ILogger<ConvertNewEpisodes> _logger = new Logger<ConvertNewEpisodes>(new LoggerFactory());
 
    
    public static RedirectLink? GetRedirectLink(string url)
    {
        try
        {
            const string sql = @"Select * From Redirects WHERE OriginalLink = @url";
            return SqLite.Connection().QueryFirst<RedirectLink>(sql, new { url = url });
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            return null;
        }
    }
    public static RedirectLink GetRedirectLink(Guid id)
    {
        try
        {
            const string sql = @"Select * From Redirects WHERE Id = @id";
            return SqLite.Connection().QueryFirst<RedirectLink>(sql, new { id = id.ToString().ToLowerInvariant() });
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message); 
            throw new ApplicationException("No redirect found");
        }
    }
    public static void CreateRedirectLink(RedirectLink link)
    {
        const string sql = @"INSERT INTO Redirects (OriginalLink,Id) VALUES (@url,@id)";
        var rows = SqLite.Connection().Execute(sql, new { url = link.OriginalLink , id = link.Id });
        if (rows != 1)
        {
            throw new ApplicationException("Unable to insert redirect");
        }
    }

    public static RedirectLink GetOrCreateRedirectLink(string url)
    {
        var redirectLink = GetRedirectLink(url);
        if (redirectLink is not null)
        {
            return (RedirectLink)redirectLink;
        }
        _logger.LogInformation($"Creating new redirect link for {url}");
        var tmpLink = new RedirectLink(url, Guid.NewGuid());
        CreateRedirectLink(tmpLink);
        return tmpLink;
    }
}