using Dai_Lete.Services;
using Microsoft.Data.Sqlite;

namespace Dai_Lete.Repositories;

public static class SqLite
{
    private static IDatabaseService? _databaseService;

    public static void Initialize(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    public static SqliteConnection Connection()
    {
        if (_databaseService is null)
        {
            throw new InvalidOperationException("Database service not initialized. Call SqLite.Initialize() first.");
        }

        // Note: This is a temporary bridge method to maintain compatibility
        // In the future, all callers should be updated to use async methods directly
        return _databaseService.GetConnectionAsync().GetAwaiter().GetResult();
    }

    public static async Task<SqliteConnection> ConnectionAsync()
    {
        if (_databaseService is null)
        {
            throw new InvalidOperationException("Database service not initialized. Call SqLite.Initialize() first.");
        }

        return await _databaseService.GetConnectionAsync();
    }
}