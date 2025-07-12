using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dapper;

namespace Dai_Lete.Services;

public class RedirectService
{
    private readonly ILogger<RedirectService> _logger;

    public RedirectService(ILogger<RedirectService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
 
    
    public async Task<RedirectLink?> GetRedirectLinkAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        try
        {
            const string sql = @"SELECT * FROM Redirects WHERE OriginalLink = @url";
            using var connection = SqLite.Connection();
            return await connection.QueryFirstOrDefaultAsync<RedirectLink>(sql, new { url });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get redirect link for URL: {Url}", url);
            return null;
        }
    }

    public async Task<RedirectLink> GetRedirectLinkAsync(Guid id)
    {
        try
        {
            const string sql = @"SELECT * FROM Redirects WHERE Id = @id";
            using var connection = SqLite.Connection();
            var result = await connection.QueryFirstOrDefaultAsync<RedirectLink?>(sql, new { id = id.ToString().ToLowerInvariant() });
            
            if (!result.HasValue)
            {
                throw new ArgumentException($"No redirect found with ID: {id}", nameof(id));
            }
            
            return result.Value;
        }
        catch (Exception ex) when (!(ex is ArgumentException))
        {
            _logger.LogError(ex, "Failed to get redirect link for ID: {Id}", id);
            throw new InvalidOperationException($"Failed to retrieve redirect link for ID: {id}", ex);
        }
    }

    public async Task CreateRedirectLinkAsync(RedirectLink link)
    {
        try
        {
            const string sql = @"INSERT INTO Redirects (OriginalLink, Id) VALUES (@url, @id)";
            using var connection = SqLite.Connection();
            var rows = await connection.ExecuteAsync(sql, new { url = link.OriginalLink, id = link.Id });
            
            if (rows != 1)
            {
                throw new InvalidOperationException("Failed to insert redirect link");
            }
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Failed to create redirect link for URL: {Url}", link.OriginalLink);
            throw new InvalidOperationException($"Failed to create redirect link for URL: {link.OriginalLink}", ex);
        }
    }

    public async Task<RedirectLink> GetOrCreateRedirectLinkAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        try
        {
            var existingLink = await GetRedirectLinkAsync(url);
            if (existingLink.HasValue)
            {
                return existingLink.Value;
            }

            _logger.LogInformation("Creating new redirect link for {Url}", url);
            var newLink = new RedirectLink(url, Guid.NewGuid());
            await CreateRedirectLinkAsync(newLink);
            return newLink;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create redirect link for URL: {Url}", url);
            throw;
        }
    }
}