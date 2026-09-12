using IPAbuyer.Core.Services.Purchases;
using Microsoft.Data.Sqlite;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Diagnostics;
using System.Globalization;

namespace IPAbuyer.Core.Data.PurchasedApps
{
    public class PurchasedAppDb
    {
        private static readonly ResourceLoader Loader = new();
        private static string _dbPath = string.Empty;
        private static string _connectionString = string.Empty;

        public static void InitDb()
        {
            Database database = new Database();
            _dbPath = database.appDb ?? throw new InvalidOperationException(L("PurchasedAppDb/Error/DatabasePathNotInitialized"));
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

                var syncStateCmd = conn.CreateCommand();
                syncStateCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS SyncState (
                        Account TEXT PRIMARY KEY,
                        LastSuccessSyncUtc TEXT NOT NULL,
                        LastAttemptSyncUtc TEXT NOT NULL
                    )";
                syncStateCmd.ExecuteNonQuery();

                MigrateSchema(conn);
            }
        }

        /// <summary>
        /// 保存已购买的应用
        /// </summary>
        /// <param name="appID">应用ID (bundleID)</param>
        /// <param name="account">购买账户</param>
        /// <param name="status">状态：已购买 或 已拥有</param>
        public static void SavePurchasedApp(string appID, string account, string? status = null)
        {
            status ??= PurchaseRecordStatus.Purchased;
            if (string.IsNullOrWhiteSpace(appID) || string.IsNullOrWhiteSpace(account))
            {
                Debug.WriteLine(L("PurchasedAppDb/Debug/AppIdOrAccountRequired"));
                return;
            }

            if (!PurchaseRecordStatus.TryNormalize(status, out string normalizedStatus))
            {
                throw new ArgumentException("The purchase record status is invalid.", nameof(status));
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
                updateCmd.Parameters.AddWithValue("$status", normalizedStatus);
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
                    insertCmd.Parameters.AddWithValue("$status", normalizedStatus);
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
                        var status = reader.IsDBNull(1) ? L("Common/Status/Purchased") : reader.GetString(1);
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

        private static void MigrateSchema(SqliteConnection connection)
        {
            using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version";
            long version = Convert.ToInt64(versionCommand.ExecuteScalar());
            if (version >= 2)
            {
                return;
            }

            using SqliteTransaction transaction = connection.BeginTransaction();
            try
            {
                if (version < 1)
                {
                    MigrateStatusTokens(connection, transaction);
                }

                // v2：合并“已拥有”到“已购买”——已拥有信息改由 list-purchases 同步提供。
                using var mergeCommand = connection.CreateCommand();
                mergeCommand.Transaction = transaction;
                mergeCommand.CommandText = "UPDATE PurchasedApp SET Status = $status WHERE Status <> $status";
                mergeCommand.Parameters.AddWithValue("$status", PurchaseRecordStatus.Purchased);
                mergeCommand.ExecuteNonQuery();

                using var setVersionCommand = connection.CreateCommand();
                setVersionCommand.Transaction = transaction;
                setVersionCommand.CommandText = "PRAGMA user_version = 2";
                setVersionCommand.ExecuteNonQuery();
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static void MigrateStatusTokens(SqliteConnection connection, SqliteTransaction transaction)
        {
            var records = new List<(long Id, string? Status)>();
            using (var selectCommand = connection.CreateCommand())
            {
                selectCommand.Transaction = transaction;
                selectCommand.CommandText = "SELECT Id, Status FROM PurchasedApp";
                using SqliteDataReader reader = selectCommand.ExecuteReader();
                while (reader.Read())
                {
                    records.Add((reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
                }
            }

            foreach ((long id, string? status) in records)
            {
                using var changeCommand = connection.CreateCommand();
                changeCommand.Transaction = transaction;
                if (PurchaseRecordStatus.TryNormalize(status, out string normalizedStatus))
                {
                    changeCommand.CommandText = "UPDATE PurchasedApp SET Status = $status WHERE Id = $id";
                    changeCommand.Parameters.AddWithValue("$status", normalizedStatus);
                }
                else
                {
                    changeCommand.CommandText = "DELETE FROM PurchasedApp WHERE Id = $id";
                }

                changeCommand.Parameters.AddWithValue("$id", id);
                changeCommand.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 批量将 App 标记为已购买（list-purchases 同步写入路径）。
        /// </summary>
        /// <returns>写入的记录数</returns>
        public static int BulkMarkPurchased(IEnumerable<string> appIds, string account)
        {
            if (appIds == null || string.IsNullOrWhiteSpace(account))
            {
                return 0;
            }

            EnsureDatabaseReady();
            string normalizedAccount = NormalizeAccount(account);
            int written = 0;

            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using SqliteTransaction transaction = conn.BeginTransaction();
                foreach (string appId in appIds)
                {
                    if (string.IsNullOrWhiteSpace(appId))
                    {
                        continue;
                    }

                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO PurchasedApp (AppID, Account, Status)
                        VALUES ($appid, $account, $status)
                        ON CONFLICT(AppID, Account)
                        DO UPDATE SET Status = $status";
                    cmd.Parameters.AddWithValue("$appid", NormalizeAppId(appId));
                    cmd.Parameters.AddWithValue("$account", normalizedAccount);
                    cmd.Parameters.AddWithValue("$status", PurchaseRecordStatus.Purchased);
                    written += cmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }

            return written;
        }

        /// <summary>
        /// 读取账户上次成功同步时间（UTC）；从未同步时返回 null。
        /// </summary>
        public static DateTime? GetLastSuccessfulSyncUtc(string account)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                return null;
            }

            EnsureDatabaseReady();
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT LastSuccessSyncUtc FROM SyncState WHERE Account = $account";
                cmd.Parameters.AddWithValue("$account", NormalizeAccount(account));
                object? result = cmd.ExecuteScalar();
                string? text = result?.ToString();
                return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value)
                    ? value
                    : null;
            }
        }

        /// <summary>
        /// 记录一次同步尝试；成功时同时更新上次成功同步时间，失败时保留原值。
        /// </summary>
        public static void RecordSyncAttempt(string account, bool succeeded, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                return;
            }

            EnsureDatabaseReady();
            string nowText = utcNow.ToString("o", CultureInfo.InvariantCulture);
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO SyncState (Account, LastSuccessSyncUtc, LastAttemptSyncUtc)
                    VALUES ($account, CASE WHEN $succeeded THEN $now ELSE '' END, $now)
                    ON CONFLICT(Account) DO UPDATE SET
                        LastSuccessSyncUtc = CASE WHEN $succeeded THEN $now ELSE SyncState.LastSuccessSyncUtc END,
                        LastAttemptSyncUtc = $now";
                cmd.Parameters.AddWithValue("$account", NormalizeAccount(account));
                cmd.Parameters.AddWithValue("$succeeded", succeeded);
                cmd.Parameters.AddWithValue("$now", nowText);
                cmd.ExecuteNonQuery();
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

        private static string L(string key)
        {
            return Loader.GetString(key);
        }
    }
}
