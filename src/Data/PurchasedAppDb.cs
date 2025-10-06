using Microsoft.Data.Sqlite;

namespace IPAbuyer.Data
{
    public class PurchasedAppDb
    {
        private static string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "purchased_apps.db");
        private static string connStr = $"Data Source={dbPath}";

        public static void InitDb()
        {
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS PurchasedApp (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        BundleID TEXT,
                        Name TEXT,
                        Version TEXT
                    )";
                cmd.ExecuteNonQuery();
            }
        }

        public static void SavePurchasedApp(string bundleID, string name, string version)
        {
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO PurchasedApp (BundleID, Name, Version) VALUES ($bid, $name, $ver)";
                cmd.Parameters.AddWithValue("$bid", bundleID);
                cmd.Parameters.AddWithValue("$name", name);
                cmd.Parameters.AddWithValue("$ver", version);
                cmd.ExecuteNonQuery();
            }
        }

        public static List<(string bundleID, string name, string version)> GetPurchasedApps()
        {
            var list = new List<(string, string, string)>();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT BundleID, Name, Version FROM PurchasedApp";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
                    }
                }
            }
            return list;
        }

        public static void ClearAllPurchasedApps()
        {
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM PurchasedApp";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
