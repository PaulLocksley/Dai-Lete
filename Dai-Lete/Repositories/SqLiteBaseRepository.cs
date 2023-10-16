using Dapper;

namespace Dai_Lete.Repositories;
using Microsoft.Data.Sqlite;

public class SqLite
{
        private static bool _dbSetup = false;
        public static string DbFile
        {
            get { return Environment.CurrentDirectory + $"{Path.DirectorySeparatorChar}Podcasts.sqlite"; }//todo config
        }

        private static void DbSetup()
        {
            var tmpConnection = new SqliteConnection("Data Source=" + DbFile);
            var sql = @"CREATE TABLE IF NOT EXISTS Podcasts (
                    Id GUID PRIMARY KEY,
                    InUri text
                )";
            tmpConnection.Execute(sql);
            sql  = @"CREATE TABLE IF NOT EXISTS Episodes (
                    Id GUID PRIMARY KEY,
                    PodcastId  text,
                    FileSize INTEGER
                )";
            tmpConnection.Execute(sql);
            _dbSetup = true;
        }
        public static SqliteConnection Connection()
        {
            if (!_dbSetup)
            {
                DbSetup();
            }
            return new SqliteConnection("Data Source=" + DbFile);
        }
        
        
}