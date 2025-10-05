using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace IPAbuyer.Data
{
    public class AccountHistoryDb
    {
        // 标记是否已退出登录
        private static string logoutFlagPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "account_logout.flag");
        public static void SetLogoutFlag()
        {
            File.WriteAllText(logoutFlagPath, "logout");
        }
        public static void ClearLogoutFlag()
        {
            if (File.Exists(logoutFlagPath)) File.Delete(logoutFlagPath);
        }
        public static bool IsLogoutFlag()
        {
            return File.Exists(logoutFlagPath);
        }
        // 清理重复邮箱，只保留最新一条
        public static void CleanupDuplicateAccounts()
        {
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"DELETE FROM AccountHistory WHERE Id NOT IN (SELECT MAX(Id) FROM AccountHistory GROUP BY Email)";
                cmd.ExecuteNonQuery();
            }
        }
        private static string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "account_history.db");
        private static string connStr = $"Data Source={dbPath}";

        public static void InitDb()
        {
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AccountHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Email TEXT UNIQUE,
                        Password TEXT
                    )";
                cmd.ExecuteNonQuery();
            }
        }

        public static void SaveAccount(string email, string password)
        {
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO AccountHistory (Email, Password) VALUES ($email, $password)";
                cmd.Parameters.AddWithValue("$email", email);
                cmd.Parameters.AddWithValue("$password", password);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (SqliteException ex)
                {
                    // 如果唯一约束冲突则更新密码
                    if (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
                    {
                        var updateCmd = conn.CreateCommand();
                        updateCmd.CommandText = "UPDATE AccountHistory SET Password = $password WHERE Email = $email";
                        updateCmd.Parameters.AddWithValue("$email", email);
                        updateCmd.Parameters.AddWithValue("$password", password);
                        updateCmd.ExecuteNonQuery();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            // 每次保存后清理重复
            CleanupDuplicateAccounts();
        }

        public static List<(string email, string password)> GetAccounts()
        {
            var list = new List<(string, string)>();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Email, Password FROM AccountHistory";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add((reader.GetString(0), reader.GetString(1)));
                    }
                }
            }
            return list;
        }
    }
}
