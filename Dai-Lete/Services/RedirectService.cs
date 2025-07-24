using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dapper;

namespace Dai_Lete.Services;

public class RedirectService
{
    private readonly ILogger<RedirectService> _logger;
    private readonly IDatabaseService _databaseService;

    public RedirectService(ILogger<RedirectService> logger, IDatabaseService databaseService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }


    public async Task<RedirectLink?> GetRedirectLinkAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        try
        {
            const string sql = @"SELECT * FROM Redirects WHERE OriginalLink = @url";
            using var connection = await _databaseService.GetConnectionAsync();
            var redirectLink = await connection.QueryFirstOrDefaultAsync<RedirectLink?>(sql, new { url });
            if (!redirectLink.HasValue)
            {
                _logger.LogWarning("Redirect not found for URL: {Url}", url);
                
                // Dump all redirects for debugging
                const string dumpSql = @"SELECT Id, OriginalLink FROM Redirects LIMIT 10";
                var allRedirects = await connection.QueryAsync(dumpSql);
                _logger.LogWarning("Current redirects in database (first 10):");
                foreach (var redirect in allRedirects)
                {
                    _logger.LogWarning("  ID: {Id}, URL: {Url}", (string)redirect.Id, (string)redirect.OriginalLink);
                }
                
                // Also check total count
                const string countSql = @"SELECT COUNT(*) FROM Redirects";
                var totalCount = await connection.QuerySingleAsync<int>(countSql);
                _logger.LogWarning("Total redirects in database: {Count}", totalCount);
                
                return null;
            }

            return redirectLink;
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
            var searchId = id.ToString().ToLowerInvariant();
            _logger.LogInformation("RedirectService: Searching for ID '{SearchId}' in database", searchId);
            
            const string sql = @"SELECT Id, OriginalLink FROM Redirects WHERE Id = @id";
            using var connection = await _databaseService.GetConnectionAsync();
            var result = await connection.QueryFirstOrDefaultAsync<RedirectLink>(sql, new { id = searchId });

            if (string.IsNullOrEmpty(result.Id))
            {
                _logger.LogWarning("Redirect not found for ID: {Id}", id);
                
                // Dump all redirects for debugging
                const string dumpSql = @"SELECT Id, OriginalLink FROM Redirects LIMIT 10";
                var allRedirects = await connection.QueryAsync(dumpSql);
                _logger.LogWarning("Current redirects in database (first 10):");
                foreach (var redirect in allRedirects)
                {
                    _logger.LogWarning("  ID: '{Id}' (length: {Length}), URL: {Url}", (string)redirect.Id, ((string)redirect.Id).Length, (string)redirect.OriginalLink);
                }
                
                // Also check total count
                const string countSql = @"SELECT COUNT(*) FROM Redirects";
                var totalCount = await connection.QuerySingleAsync<int>(countSql);
                _logger.LogWarning("Total redirects in database: {Count}", totalCount);
                
                // Check if we're looking for the right format
                _logger.LogWarning("Looking for ID in lowercase format: '{LowercaseId}' (length: {Length})", searchId, searchId.Length);
                
                // Check for exact match with different case or formatting
                const string searchSql = @"SELECT Id, OriginalLink FROM Redirects WHERE LOWER(Id) = LOWER(@searchId)";
                var caseInsensitiveResult = await connection.QueryFirstOrDefaultAsync(searchSql, new { searchId });
                if (caseInsensitiveResult != null)
                {
                    _logger.LogWarning("Found case-insensitive match: ID '{FoundId}' vs searched '{SearchId}'", (string)caseInsensitiveResult.Id, searchId);
                }
                
                throw new ArgumentException($"No redirect found with ID: {id}", nameof(id));
            }

            return result;
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
            using var connection = await _databaseService.GetConnectionAsync();
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