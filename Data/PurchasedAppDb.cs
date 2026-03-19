using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using System.Diagnostics;
using IPAbuyer.Models;

namespace IPAbuyer.Data
{
    public class PurchasedAppDb
    {
        private static string _dbPath = string.Empty;
        private static string _connectionString = string.Empty;

        public static void InitDb()
        {
            Database database = new Database();
            _dbPath = database.appDb ?? throw new InvalidOperationException("App 数据库路径未初始化");
            _connectionString = $"Data Source={_dbPath}";
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();

                // 创建新表结构（如果不存在）
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS PurchasedApp (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        AppID TEXT NOT NULL,
                        Account TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        UNIQUE(AppID, Account)
                    )";
                cmd.ExecuteNonQuery();

                // 创建索引以提高查询性能
                var indexCmd = conn.CreateCommand();
                indexCmd.CommandText = @"
                    CREATE INDEX IF NOT EXISTS idx_appid_account 
                    ON PurchasedApp(AppID, Account)";
                indexCmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 保存已购买的应用
        /// </summary>
        /// <param name="appID">应用ID (bundleID)</param>
        /// <param name="account">购买账户</param>
        /// <param name="status">状态：已购买 或 已拥有</param>
        public static void SavePurchasedApp(string appID, string account, string status = "已购买")
        {
            if (string.IsNullOrWhiteSpace(appID) || string.IsNullOrWhiteSpace(account))
            {
                Debug.WriteLine("AppID 或 Account 不能为空");
                return;
            }

            EnsureDatabaseReady();
            string normalizedAppId = NormalizeAppId(appID);
            string normalizedAccount = NormalizeAccount(account);

            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                // 先按规范化键更新，兼容历史大小写/空白差异数据。
                var updateCmd = conn.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE PurchasedApp
                    SET Status = $status
                    WHERE LOWER(TRIM(AppID)) = $appid
                      AND LOWER(TRIM(Account)) = $account";
                updateCmd.Parameters.AddWithValue("$appid", normalizedAppId);
                updateCmd.Parameters.AddWithValue("$account", normalizedAccount);
                updateCmd.Parameters.AddWithValue("$status", status);
                int affected = updateCmd.ExecuteNonQuery();

                if (affected == 0)
                {
                    var insertCmd = conn.CreateCommand();
                    insertCmd.CommandText = @"
                        INSERT INTO PurchasedApp (AppID, Account, Status)
                        VALUES ($appid, $account, $status)
                        ON CONFLICT(AppID, Account)
                        DO UPDATE SET Status = $status";
                    insertCmd.Parameters.AddWithValue("$appid", normalizedAppId);
                    insertCmd.Parameters.AddWithValue("$account", normalizedAccount);
                    insertCmd.Parameters.AddWithValue("$status", status);
                    insertCmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 获取指定账户的所有已购买应用
        /// </summary>
        /// <param name="account">账户名</param>
        /// <returns>应用列表 (AppID, Status)</returns>
        public static List<(string appID, string status)> GetPurchasedApps(string account)
        {
            var list = new List<(string, string)>();

            if (string.IsNullOrWhiteSpace(account))
            {
                return list;
            }

            EnsureDatabaseReady();
            string normalizedAccount = NormalizeAccount(account);
            var deduplicated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT AppID, Status
                    FROM PurchasedApp
                    WHERE LOWER(TRIM(Account)) = $account
                    ORDER BY Id";
                cmd.Parameters.AddWithValue("$account", normalizedAccount);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var appId = reader.IsDBNull(0) ? string.Empty : NormalizeAppId(reader.GetString(0));
                        var status = reader.IsDBNull(1) ? "已购买" : reader.GetString(1);
                        if (string.IsNullOrWhiteSpace(appId))
                        {
                            continue;
                        }

                        deduplicated[appId] = status;
                    }
                }
            }

            foreach (var pair in deduplicated)
            {
                list.Add((pair.Key, pair.Value));
            }
            return list;
        }

        /// <summary>
        /// 检查应用是否已购买
        /// </summary>
        /// <param name="appID">应用ID</param>
        /// <param name="account">账户名</param>
        /// <returns>状态：null(未购买), "已购买", "已拥有"</returns>
        public static string? GetAppStatus(string appID, string account)
        {
            if (string.IsNullOrWhiteSpace(appID) || string.IsNullOrWhiteSpace(account))
            {
                return null;
            }

            EnsureDatabaseReady();
            string normalizedAppId = NormalizeAppId(appID);
            string normalizedAccount = NormalizeAccount(account);

            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT Status
                    FROM PurchasedApp
                    WHERE LOWER(TRIM(AppID)) = $appid
                      AND LOWER(TRIM(Account)) = $account
                    ORDER BY Id DESC
                    LIMIT 1";
                cmd.Parameters.AddWithValue("$appid", normalizedAppId);
                cmd.Parameters.AddWithValue("$account", normalizedAccount);

                var result = cmd.ExecuteScalar();
                return result?.ToString();
            }
        }

        public static void RemovePurchasedApp(string appID, string account)
        {
            if (string.IsNullOrWhiteSpace(appID) || string.IsNullOrWhiteSpace(account))
            {
                return;
            }

            EnsureDatabaseReady();
            string normalizedAppId = NormalizeAppId(appID);
            string normalizedAccount = NormalizeAccount(account);

            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM PurchasedApp
                    WHERE LOWER(TRIM(AppID)) = $appid
                      AND LOWER(TRIM(Account)) = $account";
                cmd.Parameters.AddWithValue("$appid", normalizedAppId);
                cmd.Parameters.AddWithValue("$account", normalizedAccount);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 清除指定账户的所有已购买记录
        /// </summary>
        /// <param name="account">账户名，如果为空则清除所有记录</param>
        public static void ClearPurchasedApps(string? account = null)
        {
            EnsureDatabaseReady();
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();

                if (string.IsNullOrWhiteSpace(account))
                {
                    cmd.CommandText = "DELETE FROM PurchasedApp";
                }
                else
                {
                    cmd.CommandText = "DELETE FROM PurchasedApp WHERE LOWER(TRIM(Account)) = $account";
                    cmd.Parameters.AddWithValue("$account", NormalizeAccount(account));
                }

                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 获取所有已购买应用数量
        /// </summary>
        public static int GetTotalCount(string? account = null)
        {
            EnsureDatabaseReady();
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();

                if (string.IsNullOrWhiteSpace(account))
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM PurchasedApp";
                }
                else
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM PurchasedApp WHERE LOWER(TRIM(Account)) = $account";
                    cmd.Parameters.AddWithValue("$account", NormalizeAccount(account));
                }

                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 0;
            }
        }

        private static void EnsureDatabaseReady()
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                InitDb();
            }
        }

        private static string NormalizeAccount(string account)
        {
            return account.Trim().ToLowerInvariant();
        }

        private static string NormalizeAppId(string appId)
        {
            return appId.Trim().ToLowerInvariant();
        }

        private static string ResolveDataDirectory()
        {
            try
            {
                if (ApplicationData.Current != null)
                {
                    string localPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "IPAbuyer");
                    Directory.CreateDirectory(localPath);
                    return localPath;
                }
            }
            catch
            {
                // ignore and fall back
            }

            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPAbuyer");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
