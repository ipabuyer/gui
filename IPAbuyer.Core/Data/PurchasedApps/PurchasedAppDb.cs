using IPAbuyer.Core.Native;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Diagnostics;

namespace IPAbuyer.Core.Data.PurchasedApps
{
    /// <summary>
    /// 已购买记录数据库（PurchasedAppDb.db）的门面：实际存储由 Rust core 的
    /// SQLite 实现承担（schema 与迁移见 Core 仓库 DEVELOPMENT.md 第 8 节）。
    /// Core 的数据库句柄非线程安全，这里以全局锁串行化所有访问；
    /// list-purchases 同步在 Core 侧自建独立连接，不经过本句柄。
    /// </summary>
    public class PurchasedAppDb
    {
        private static readonly ResourceLoader Loader = new();
        private static readonly object SyncRoot = new();
        private static IntPtr _handle = IntPtr.Zero;
        private static string _dbPath = string.Empty;

        public static void InitDb()
        {
            lock (SyncRoot)
            {
                if (_handle != IntPtr.Zero)
                {
                    return;
                }

                Database database = new Database();
                _dbPath = database.appDb ?? throw new InvalidOperationException(L("PurchasedAppDb/Error/DatabasePathNotInitialized"));
                _handle = CoreNative.DbOpen(_dbPath);
            }
        }

        /// <summary>数据库文件完整路径（list-purchases 同步句柄在 Core 侧自建连接时使用）。</summary>
        internal static string GetDatabasePath()
        {
            lock (SyncRoot)
            {
                EnsureHandleLocked();
                return _dbPath;
            }
        }

        /// <summary>
        /// 保存已购买的应用
        /// </summary>
        /// <param name="appID">应用ID (bundleID)</param>
        /// <param name="account">购买账户</param>
        /// <param name="status">状态：已购买（owned 已并入）</param>
        public static void SavePurchasedApp(string appID, string account, string? status = null)
        {
            if (string.IsNullOrWhiteSpace(appID) || string.IsNullOrWhiteSpace(account))
            {
                Debug.WriteLine(L("PurchasedAppDb/Debug/AppIdOrAccountRequired"));
                return;
            }

            WithHandle(handle => CoreNative.DbSavePurchasedApp(handle, appID, account, status));
        }

        /// <summary>
        /// 获取指定账户的所有已购买应用
        /// </summary>
        /// <param name="account">账户名</param>
        /// <returns>应用列表 (AppID, Status)</returns>
        public static List<(string appID, string status)> GetPurchasedApps(string account)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                return new List<(string, string)>();
            }

            return WithHandle(handle => CoreNative.DbGetPurchasedApps(handle, account)
                .Select(record => (record.AppId, record.Status))
                .ToList());
        }

        /// <summary>
        /// 检查应用是否已购买
        /// </summary>
        /// <param name="appID">应用ID</param>
        /// <param name="account">账户名</param>
        /// <returns>状态：null(未购买) 或 Core 归一化状态（"purchased"）</returns>
        public static string? GetAppStatus(string appID, string account)
        {
            if (string.IsNullOrWhiteSpace(appID) || string.IsNullOrWhiteSpace(account))
            {
                return null;
            }

            return WithHandle(handle => CoreNative.DbGetAppStatus(handle, appID, account));
        }

        public static void RemovePurchasedApp(string appID, string account)
        {
            if (string.IsNullOrWhiteSpace(appID) || string.IsNullOrWhiteSpace(account))
            {
                return;
            }

            WithHandle(handle => CoreNative.DbRemovePurchasedApp(handle, appID, account));
        }

        /// <summary>
        /// 清除指定账户的所有已购买记录
        /// </summary>
        /// <param name="account">账户名，如果为空则清除所有记录</param>
        public static void ClearPurchasedApps(string? account = null)
        {
            WithHandle(handle => CoreNative.DbClearPurchasedApps(handle, account));
        }

        /// <summary>
        /// 获取所有已购买应用数量
        /// </summary>
        public static int GetTotalCount(string? account = null)
        {
            return WithHandle(handle => (int)CoreNative.DbGetTotalCount(handle, account));
        }

        /// <summary>
        /// 批量将 App 标记为已购买（list-purchases 同步写入路径）。
        /// </summary>
        /// <returns>传入的记录数（Core 侧单事务写入）</returns>
        public static int BulkMarkPurchased(IEnumerable<string> appIds, string account)
        {
            if (appIds == null || string.IsNullOrWhiteSpace(account))
            {
                return 0;
            }

            List<string> bundleIds = appIds.ToList();
            WithHandle(handle => CoreNative.DbBulkMarkPurchased(handle, bundleIds, account));
            return bundleIds.Count;
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

            return WithHandle<DateTime?>(handle =>
            {
                string? text = CoreNative.DbGetLastSyncUtc(handle, account);
                return DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out DateTime value)
                    ? value
                    : null;
            });
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

            WithHandle(handle => CoreNative.DbRecordSyncAttempt(handle, account, succeeded));
        }

        private static void WithHandle(Action<IntPtr> action)
        {
            lock (SyncRoot)
            {
                EnsureHandleLocked();
                action(_handle);
            }
        }

        private static T WithHandle<T>(Func<IntPtr, T> action)
        {
            lock (SyncRoot)
            {
                EnsureHandleLocked();
                return action(_handle);
            }
        }

        private static void EnsureHandleLocked()
        {
            if (_handle != IntPtr.Zero)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_dbPath))
            {
                InitDb();
                return;
            }

            _handle = CoreNative.DbOpen(_dbPath);
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }
    }
}
