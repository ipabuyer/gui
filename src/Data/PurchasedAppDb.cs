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
                        Version TEXT,
                        Status TEXT
                    )";
                cmd.ExecuteNonQuery();

                // 向后兼容：如果旧表没有 Status 列，尝试添加（SQLite 不支持 ALTER ADD COLUMN IF NOT EXISTS 旧版本，
                // 但这个语句在多数环境下可行；如果失败则忽略）
                try
                {
                    var alter = conn.CreateCommand();
                    alter.CommandText = "ALTER TABLE PurchasedApp ADD COLUMN Status TEXT";
                    alter.ExecuteNonQuery();
                }
                catch { }
            }
        }

        public static void SavePurchasedApp(string bundleID, string name, string version, string status = "已购买")
        {
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO PurchasedApp (BundleID, Name, Version, Status) VALUES ($bid, $name, $ver, $status)";
                cmd.Parameters.AddWithValue("$bid", bundleID);
                cmd.Parameters.AddWithValue("$name", name);
                cmd.Parameters.AddWithValue("$ver", version);
                cmd.Parameters.AddWithValue("$status", status);
                cmd.ExecuteNonQuery();
            }
        }

        public static List<(string bundleID, string name, string version, string status)> GetPurchasedApps()
        {
            var list = new List<(string, string, string, string)>();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT BundleID, Name, Version, COALESCE(Status, '') FROM PurchasedApp";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var b = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                        var n = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        var v = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        var s = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                        list.Add((b, n, v, s));
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
