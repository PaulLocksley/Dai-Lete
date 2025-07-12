using Microsoft.Data.Sqlite;
using Dapper;

namespace Dai_Lete.Services;

public interface IDatabaseService
{
    Task<SqliteConnection> GetConnectionAsync();
    Task InitializeDatabaseAsync();
}

public class DatabaseService : IDatabaseService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseService> _logger;
    private bool _isInitialized;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SqliteConnection> GetConnectionAsync()
    {
        if (!_isInitialized)
        {
            await InitializeDatabaseAsync();
        }

        var connectionString = GetConnectionString();
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task InitializeDatabaseAsync()
    {
        if (_isInitialized) return;

        await _initSemaphore.WaitAsync();
        try
        {
            if (_isInitialized) return;

            _logger.LogInformation("Initializing database");

            var dbPath = GetDatabasePath();
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created database directory: {Directory}", directory);
            }

            using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync();

            await CreateTablesAsync(connection);

            _isInitialized = true;
            _logger.LogInformation("Database initialized successfully at: {DatabasePath}", dbPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            throw;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    private async Task CreateTablesAsync(SqliteConnection connection)
    {
        var createPodcastsTable = @"
            CREATE TABLE IF NOT EXISTS Podcasts (
                Id TEXT PRIMARY KEY,
                InUri TEXT NOT NULL
            )";

        var createEpisodesTable = @"
            CREATE TABLE IF NOT EXISTS Episodes (
                Id TEXT PRIMARY KEY,
                PodcastId TEXT NOT NULL,
                FileSize INTEGER,
                FOREIGN KEY (PodcastId) REFERENCES Podcasts(Id)
            )";

        var createRedirectsTable = @"
            CREATE TABLE IF NOT EXISTS Redirects (
                Id TEXT PRIMARY KEY,
                OriginalLink TEXT NOT NULL
            )";

        await connection.ExecuteAsync(createPodcastsTable);
        await connection.ExecuteAsync(createEpisodesTable);
        await connection.ExecuteAsync(createRedirectsTable);

        _logger.LogDebug("Database tables created/verified");
    }

    private string GetDatabasePath()
    {
        var configPath = _configuration["Database:Path"];
        if (!string.IsNullOrEmpty(configPath))
        {
            return configPath;
        }

        // Fallback to default path
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDirectory, "Podcasts", "Podcasts.sqlite");
    }

    private string GetConnectionString()
    {
        var dbPath = GetDatabasePath();
        return $"Data Source={dbPath}";
    }
}