using Dapper;
using Microsoft.Data.Sqlite;

namespace Dai_Lete.Services;

internal static class DatabaseMigrations
{
    public static async Task ApplyAsync(SqliteConnection connection, ILogger logger)
    {
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS __Migrations (
                Version INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                AppliedAt TEXT NOT NULL
            )");

        var appliedVersions = (await connection.QueryAsync<int>("SELECT Version FROM __Migrations")).ToHashSet();

        foreach (var migration in GetMigrations().OrderBy(m => m.Version))
        {
            if (appliedVersions.Contains(migration.Version)) continue;

            using var transaction = connection.BeginTransaction();
            try
            {
                logger.LogInformation("Applying database migration {Version}: {Name}", migration.Version, migration.Name);
                await migration.Up(connection, transaction);
                await connection.ExecuteAsync(
                    "INSERT INTO __Migrations (Version, Name, AppliedAt) VALUES (@Version, @Name, datetime('now'))",
                    new { migration.Version, migration.Name },
                    transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        logger.LogDebug("Database migrations verified");
    }

    private static IEnumerable<Migration> GetMigrations()
    {
        yield return new Migration(1, "initial_schema", async (connection, transaction) =>
        {
            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS Podcasts (
                    Id GUID PRIMARY KEY,
                    InUri TEXT NOT NULL
                )", transaction: transaction);

            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS Episodes (
                    Id GUID PRIMARY KEY,
                    PodcastId GUID NOT NULL,
                    FileSize INTEGER,
                    InitialLengthSeconds REAL,
                    ProcessedLengthSeconds REAL,
                    FOREIGN KEY (PodcastId) REFERENCES Podcasts(Id)
                )", transaction: transaction);

            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS Redirects (
                    Id GUID PRIMARY KEY,
                    OriginalLink TEXT NOT NULL
                )", transaction: transaction);
        });

        yield return new Migration(2, "episode_lengths", async (connection, transaction) =>
        {
            await EnsureColumnAsync(connection, transaction, "Episodes", "InitialLengthSeconds", "REAL");
            await EnsureColumnAsync(connection, transaction, "Episodes", "ProcessedLengthSeconds", "REAL");
        });
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, SqliteTransaction transaction, string tableName, string columnName, string columnType)
    {
        var columns = await connection.QueryAsync<string>($"SELECT name FROM pragma_table_info('{tableName}')", transaction: transaction);
        if (columns.Contains(columnName)) return;

        await connection.ExecuteAsync($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}", transaction: transaction);
    }

    private sealed record Migration(int Version, string Name, Func<SqliteConnection, SqliteTransaction, Task> Up);
}
