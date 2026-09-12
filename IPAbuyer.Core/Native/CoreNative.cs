using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace IPAbuyer.Core.Native
{
    /// <summary>Core 调用失败（非 0 状态码）时抛出；消息来自 Core 的 last_error。</summary>
    public sealed class CoreException : Exception
    {
        public int Code { get; }

        public CoreException(int code, string message)
            : base(message)
        {
            Code = code;
        }
    }

    /// <summary>
    /// 取消标志：对应 Core 侧 `*const AtomicBool`（1 字节布尔，按 4 字节整数读写）。
    /// 由宿主置位，Core 在进程等待的检查点读取。
    /// </summary>
    internal sealed class CoreCancelFlag : IDisposable
    {
        private IntPtr _ptr;

        public CoreCancelFlag()
        {
            _ptr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(_ptr, 0);
        }

        public IntPtr Handle => _ptr;

        public void Cancel()
        {
            if (_ptr != IntPtr.Zero)
            {
                Marshal.WriteInt32(_ptr, 1);
            }
        }

        public void Dispose()
        {
            if (_ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// ipabuyer_core.dll（Rust core）的 C ABI 封装。
    /// 约定见 IPAbuyer.Core 仓库 DEVELOPMENT.md 第 5 节：
    /// - 状态码 0=成功，1=无效参数，2=失败，3=panic；失败描述经 last_error（线程本地）获取；
    /// - 宿主传入字符串为 NUL 结尾 UTF-8；Core 返回字符串用毕必须 free；
    /// - 导出函数为阻塞同步调用，调用方负责放到后台线程（Task.Run）。
    /// </summary>
    internal static class CoreNative
    {
        private const string Dll = "ipabuyer_core.dll";

        public const int Ok = 0;
        public const int ErrorInvalidArgument = 1;
        public const int ErrorFailed = 2;
        public const int ErrorPanic = 3;

        // ---- 基础导出 ----

        [DllImport(Dll)]
        private static extern IntPtr ipabuyer_core_version();

        [DllImport(Dll)]
        private static extern IntPtr ipabuyer_core_last_error();

        [DllImport(Dll)]
        private static extern void ipabuyer_core_free_string(IntPtr ptr);

        // ---- 数据库 ----

        [DllImport(Dll)]
        private static extern int ipabuyer_core_db_open(IntPtr path, out IntPtr handle);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_db_close(IntPtr handle);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_db_save_purchased_app(IntPtr handle, IntPtr appId, IntPtr account, IntPtr status);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_db_get_purchased_apps(IntPtr handle, IntPtr account, out IntPtr json);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_db_get_app_status(IntPtr handle, IntPtr appId, IntPtr account, out IntPtr json);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_db_remove_purchased_app(IntPtr handle, IntPtr appId, IntPtr account);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_db_clear_purchased_apps(IntPtr handle, IntPtr account);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_db_get_total_count(IntPtr handle, IntPtr account, out long count);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_db_bulk_mark_purchased(IntPtr handle, IntPtr bundleIdsJson, IntPtr account);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_db_get_last_sync_utc(IntPtr handle, IntPtr account, out IntPtr json);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_db_record_sync_attempt(IntPtr handle, IntPtr account, int succeeded);

        // ---- 认证 ----

        [DllImport(Dll)]
        private static extern int ipabuyer_core_is_mock_account(IntPtr username, IntPtr password, out IntPtr json);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_auth_login(IntPtr exePath, IntPtr account, IntPtr password, IntPtr authCode, IntPtr passphrase, int detailedLog, IntPtr cancel, out IntPtr json);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_auth_verify_code(IntPtr exePath, IntPtr account, IntPtr password, IntPtr authCode, IntPtr passphrase, int detailedLog, IntPtr cancel, out IntPtr json);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_auth_logout(IntPtr exePath, int detailedLog, IntPtr cancel, out IntPtr json);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_auth_info(IntPtr exePath, IntPtr passphrase, int detailedLog, IntPtr cancel, out IntPtr json);

        // ---- 搜索 / 购买 ----

        [DllImport(Dll)]
        private static extern int ipabuyer_core_catalog_search(IntPtr name, long limit, IntPtr country, IntPtr purchasedAppsJson, out IntPtr json);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_purchase(IntPtr exePath, IntPtr bundleId, IntPtr passphrase, int detailedLog, IntPtr cancel, out IntPtr json);

        // ---- 同步（轮询句柄） ----

        [DllImport(Dll)]
        private static extern int ipabuyer_core_sync_create(IntPtr dbPath, IntPtr exePath, IntPtr passphrase, IntPtr account, int detailedLog, out IntPtr handle);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_sync_status(IntPtr handle, out IntPtr json);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_sync_cancel(IntPtr handle);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_sync_destroy(IntPtr handle);

        // ---- 下载队列（轮询句柄） ----

        [DllImport(Dll)]
        private static extern int ipabuyer_core_queue_create(IntPtr exePath, IntPtr outputDirectory, IntPtr passphrase, int isMock, int detailedLog, out IntPtr handle);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_queue_add(IntPtr handle, IntPtr itemJson, out int result);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_queue_remove(IntPtr handle, IntPtr bundleId);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_queue_start(IntPtr handle);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_queue_status(IntPtr handle, out IntPtr json);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_queue_cancel(IntPtr handle);

        [DllImport(Dll)]
        private static extern int ipabuyer_core_queue_destroy(IntPtr handle);

        // ---- 封装：字符串与错误处理 ----

        /// <summary>分配宿主→Core 方向的 UTF-8 字符串；null 传空指针。</summary>
        internal static IntPtr AllocUtf8(string? value)
        {
            return value == null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(value);
        }

        internal static void FreeUtf8(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }

        /// <summary>读取 Core 返回的 JSON/字符串指针并释放。</summary>
        internal static string ReadAndFreeCoreString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
            }
            finally
            {
                ipabuyer_core_free_string(ptr);
            }
        }

        /// <summary>经源生成上下文反序列化（发布启用 full trim，禁用反射序列化路径）。</summary>
        internal static T ReadAndFreeJson<T>(IntPtr ptr, JsonTypeInfo<T> typeInfo)
        {
            string text = ReadAndFreeCoreString(ptr);
            return JsonSerializer.Deserialize(text, typeInfo)
                ?? throw new CoreException(ErrorFailed, "core returned null json");
        }

        internal static string WriteJson<T>(T value, JsonTypeInfo<T> typeInfo)
        {
            return JsonSerializer.Serialize(value, typeInfo);
        }

        private static void ThrowIfFailed(int code)
        {
            if (code == Ok)
            {
                return;
            }

            string message = ReadAndFreeCoreString(ipabuyer_core_last_error());
            throw new CoreException(code, string.IsNullOrWhiteSpace(message) ? $"core call failed with code {code}" : message);
        }

        public static string GetVersion()
        {
            return ReadAndFreeCoreString(ipabuyer_core_version());
        }

        // ---- 封装：数据库 ----

        public static IntPtr DbOpen(string path)
        {
            IntPtr pathPtr = AllocUtf8(path);
            try
            {
                int code = ipabuyer_core_db_open(pathPtr, out IntPtr handle);
                if (code != Ok)
                {
                    ThrowIfFailed(code);
                }
                return handle;
            }
            finally
            {
                FreeUtf8(pathPtr);
            }
        }

        public static void DbClose(IntPtr handle)
        {
            ThrowIfFailed(ipabuyer_core_db_close(handle));
        }

        public static void DbSavePurchasedApp(IntPtr handle, string appId, string account, string? status)
        {
            IntPtr appIdPtr = AllocUtf8(appId);
            IntPtr accountPtr = AllocUtf8(account);
            IntPtr statusPtr = AllocUtf8(status);
            try
            {
                ThrowIfFailed(ipabuyer_core_db_save_purchased_app(handle, appIdPtr, accountPtr, statusPtr));
            }
            finally
            {
                FreeUtf8(appIdPtr);
                FreeUtf8(accountPtr);
                FreeUtf8(statusPtr);
            }
        }

        public static List<(string AppId, string Status)> DbGetPurchasedApps(IntPtr handle, string account)
        {
            IntPtr accountPtr = AllocUtf8(account);
            try
            {
                int code = ipabuyer_core_db_get_purchased_apps(handle, accountPtr, out IntPtr json);
                if (code != Ok)
                {
                    ThrowIfFailed(code);
                }
                return ReadAndFreeJson(json, CoreJsonContext.Default.ListCorePurchasedAppEntry)
                    .Select(entry => (entry.AppId, entry.Status))
                    .ToList();
            }
            finally
            {
                FreeUtf8(accountPtr);
            }
        }

        public static string? DbGetAppStatus(IntPtr handle, string appId, string account)
        {
            IntPtr appIdPtr = AllocUtf8(appId);
            IntPtr accountPtr = AllocUtf8(account);
            try
            {
                int code = ipabuyer_core_db_get_app_status(handle, appIdPtr, accountPtr, out IntPtr json);
                if (code != Ok)
                {
                    ThrowIfFailed(code);
                }
                return ReadAndFreeJson(json, CoreJsonContext.Default.String);
            }
            finally
            {
                FreeUtf8(appIdPtr);
                FreeUtf8(accountPtr);
            }
        }

        public static void DbRemovePurchasedApp(IntPtr handle, string appId, string account)
        {
            IntPtr appIdPtr = AllocUtf8(appId);
            IntPtr accountPtr = AllocUtf8(account);
            try
            {
                ThrowIfFailed(ipabuyer_core_db_remove_purchased_app(handle, appIdPtr, accountPtr));
            }
            finally
            {
                FreeUtf8(appIdPtr);
                FreeUtf8(accountPtr);
            }
        }

        public static void DbClearPurchasedApps(IntPtr handle, string? account)
        {
            IntPtr accountPtr = AllocUtf8(account);
            try
            {
                ThrowIfFailed(ipabuyer_core_db_clear_purchased_apps(handle, accountPtr));
            }
            finally
            {
                FreeUtf8(accountPtr);
            }
        }

        public static long DbGetTotalCount(IntPtr handle, string? account)
        {
            IntPtr accountPtr = AllocUtf8(account);
            try
            {
                ThrowIfFailed(ipabuyer_core_db_get_total_count(handle, accountPtr, out long count));
                return count;
            }
            finally
            {
                FreeUtf8(accountPtr);
            }
        }

        public static void DbBulkMarkPurchased(IntPtr handle, IEnumerable<string> bundleIds, string account)
        {
            IntPtr jsonPtr = AllocUtf8(WriteJson(bundleIds.ToList(), CoreJsonContext.Default.ListString));
            IntPtr accountPtr = AllocUtf8(account);
            try
            {
                ThrowIfFailed(ipabuyer_core_db_bulk_mark_purchased(handle, jsonPtr, accountPtr));
            }
            finally
            {
                FreeUtf8(jsonPtr);
                FreeUtf8(accountPtr);
            }
        }

        public static string? DbGetLastSyncUtc(IntPtr handle, string account)
        {
            IntPtr accountPtr = AllocUtf8(account);
            try
            {
                int code = ipabuyer_core_db_get_last_sync_utc(handle, accountPtr, out IntPtr json);
                if (code != Ok)
                {
                    ThrowIfFailed(code);
                }
                return ReadAndFreeJson(json, CoreJsonContext.Default.String);
            }
            finally
            {
                FreeUtf8(accountPtr);
            }
        }

        public static void DbRecordSyncAttempt(IntPtr handle, string account, bool succeeded)
        {
            IntPtr accountPtr = AllocUtf8(account);
            try
            {
                ThrowIfFailed(ipabuyer_core_db_record_sync_attempt(handle, accountPtr, succeeded ? 1 : 0));
            }
            finally
            {
                FreeUtf8(accountPtr);
            }
        }

        // ---- 封装：认证 ----

        public static bool IsMockAccount(string username, string password)
        {
            IntPtr usernamePtr = AllocUtf8(username);
            IntPtr passwordPtr = AllocUtf8(password);
            try
            {
                ThrowIfFailed(ipabuyer_core_is_mock_account(usernamePtr, passwordPtr, out IntPtr json));
                return ReadAndFreeJson(json, CoreJsonContext.Default.Boolean);
            }
            finally
            {
                FreeUtf8(usernamePtr);
                FreeUtf8(passwordPtr);
            }
        }

        public static CoreLoginResult AuthLogin(string exePath, string account, string password, string authCode, string passphrase, bool detailedLog, IntPtr cancel)
        {
            IntPtr exePtr = AllocUtf8(exePath);
            IntPtr accountPtr = AllocUtf8(account);
            IntPtr passwordPtr = AllocUtf8(password);
            IntPtr authCodePtr = AllocUtf8(authCode);
            IntPtr passphrasePtr = AllocUtf8(passphrase);
            try
            {
                ThrowIfFailed(ipabuyer_core_auth_login(exePtr, accountPtr, passwordPtr, authCodePtr, passphrasePtr, detailedLog ? 1 : 0, cancel, out IntPtr json));
                return ReadAndFreeJson(json, CoreJsonContext.Default.CoreLoginResult);
            }
            finally
            {
                FreeUtf8(exePtr);
                FreeUtf8(accountPtr);
                FreeUtf8(passwordPtr);
                FreeUtf8(authCodePtr);
                FreeUtf8(passphrasePtr);
            }
        }

        public static CoreLoginResult AuthVerifyCode(string exePath, string account, string password, string authCode, string passphrase, bool detailedLog, IntPtr cancel)
        {
            IntPtr exePtr = AllocUtf8(exePath);
            IntPtr accountPtr = AllocUtf8(account);
            IntPtr passwordPtr = AllocUtf8(password);
            IntPtr authCodePtr = AllocUtf8(authCode);
            IntPtr passphrasePtr = AllocUtf8(passphrase);
            try
            {
                ThrowIfFailed(ipabuyer_core_auth_verify_code(exePtr, accountPtr, passwordPtr, authCodePtr, passphrasePtr, detailedLog ? 1 : 0, cancel, out IntPtr json));
                return ReadAndFreeJson(json, CoreJsonContext.Default.CoreLoginResult);
            }
            finally
            {
                FreeUtf8(exePtr);
                FreeUtf8(accountPtr);
                FreeUtf8(passwordPtr);
                FreeUtf8(authCodePtr);
                FreeUtf8(passphrasePtr);
            }
        }

        public static CoreLogoutResult AuthLogout(string exePath, bool detailedLog, IntPtr cancel)
        {
            IntPtr exePtr = AllocUtf8(exePath);
            try
            {
                ThrowIfFailed(ipabuyer_core_auth_logout(exePtr, detailedLog ? 1 : 0, cancel, out IntPtr json));
                return ReadAndFreeJson(json, CoreJsonContext.Default.CoreLogoutResult);
            }
            finally
            {
                FreeUtf8(exePtr);
            }
        }

        public static CoreAuthInfo AuthInfo(string exePath, string? passphrase, bool detailedLog, IntPtr cancel)
        {
            IntPtr exePtr = AllocUtf8(exePath);
            IntPtr passphrasePtr = AllocUtf8(passphrase);
            try
            {
                ThrowIfFailed(ipabuyer_core_auth_info(exePtr, passphrasePtr, detailedLog ? 1 : 0, cancel, out IntPtr json));
                return ReadAndFreeJson(json, CoreJsonContext.Default.CoreAuthInfo);
            }
            finally
            {
                FreeUtf8(exePtr);
                FreeUtf8(passphrasePtr);
            }
        }

        // ---- 封装：搜索 / 购买 ----

        public static List<CoreSearchResult> CatalogSearch(string name, int limit, string country, List<CorePurchasedAppEntry>? purchasedApps)
        {
            IntPtr namePtr = AllocUtf8(name);
            IntPtr countryPtr = AllocUtf8(country);
            IntPtr purchasedPtr = AllocUtf8(
                purchasedApps == null ? null : WriteJson(purchasedApps, CoreJsonContext.Default.ListCorePurchasedAppEntry));
            try
            {
                ThrowIfFailed(ipabuyer_core_catalog_search(namePtr, limit, countryPtr, purchasedPtr, out IntPtr json));
                return ReadAndFreeJson(json, CoreJsonContext.Default.ListCoreSearchResult);
            }
            finally
            {
                FreeUtf8(namePtr);
                FreeUtf8(countryPtr);
                FreeUtf8(purchasedPtr);
            }
        }

        public static CorePurchaseResult Purchase(string exePath, string bundleId, string passphrase, bool detailedLog, IntPtr cancel)
        {
            IntPtr exePtr = AllocUtf8(exePath);
            IntPtr bundleIdPtr = AllocUtf8(bundleId);
            IntPtr passphrasePtr = AllocUtf8(passphrase);
            try
            {
                ThrowIfFailed(ipabuyer_core_purchase(exePtr, bundleIdPtr, passphrasePtr, detailedLog ? 1 : 0, cancel, out IntPtr json));
                return ReadAndFreeJson(json, CoreJsonContext.Default.CorePurchaseResult);
            }
            finally
            {
                FreeUtf8(exePtr);
                FreeUtf8(bundleIdPtr);
                FreeUtf8(passphrasePtr);
            }
        }

        // ---- 封装：同步句柄 ----

        public static IntPtr SyncCreate(string dbPath, string exePath, string passphrase, string account, bool detailedLog)
        {
            IntPtr dbPtr = AllocUtf8(dbPath);
            IntPtr exePtr = AllocUtf8(exePath);
            IntPtr passphrasePtr = AllocUtf8(passphrase);
            IntPtr accountPtr = AllocUtf8(account);
            try
            {
                int code = ipabuyer_core_sync_create(dbPtr, exePtr, passphrasePtr, accountPtr, detailedLog ? 1 : 0, out IntPtr handle);
                if (code != Ok)
                {
                    ThrowIfFailed(code);
                }
                return handle;
            }
            finally
            {
                FreeUtf8(dbPtr);
                FreeUtf8(exePtr);
                FreeUtf8(passphrasePtr);
                FreeUtf8(accountPtr);
            }
        }

        public static CoreSyncStatus SyncStatus(IntPtr handle)
        {
            ThrowIfFailed(ipabuyer_core_sync_status(handle, out IntPtr json));
            return ReadAndFreeJson(json, CoreJsonContext.Default.CoreSyncStatus);
        }

        public static void SyncCancel(IntPtr handle)
        {
            ThrowIfFailed(ipabuyer_core_sync_cancel(handle));
        }

        public static void SyncDestroy(IntPtr handle)
        {
            ThrowIfFailed(ipabuyer_core_sync_destroy(handle));
        }

        // ---- 封装：下载队列句柄 ----

        public static IntPtr QueueCreate(string exePath, string outputDirectory, string passphrase, bool isMock, bool detailedLog)
        {
            IntPtr exePtr = AllocUtf8(exePath);
            IntPtr outputPtr = AllocUtf8(outputDirectory);
            IntPtr passphrasePtr = AllocUtf8(passphrase);
            try
            {
                int code = ipabuyer_core_queue_create(exePtr, outputPtr, passphrasePtr, isMock ? 1 : 0, detailedLog ? 1 : 0, out IntPtr handle);
                if (code != Ok)
                {
                    ThrowIfFailed(code);
                }
                return handle;
            }
            finally
            {
                FreeUtf8(exePtr);
                FreeUtf8(outputPtr);
                FreeUtf8(passphrasePtr);
            }
        }

        /// <summary>入队一个条目；返回 Core 契约码：0=Added / 1=Updated / 2=Requeued / 3=Ignored。</summary>
        public static int QueueAdd(IntPtr handle, string itemJson)
        {
            IntPtr itemPtr = AllocUtf8(itemJson);
            try
            {
                ThrowIfFailed(ipabuyer_core_queue_add(handle, itemPtr, out int result));
                return result;
            }
            finally
            {
                FreeUtf8(itemPtr);
            }
        }

        public static void QueueRemove(IntPtr handle, string bundleId)
        {
            IntPtr bundleIdPtr = AllocUtf8(bundleId);
            try
            {
                ThrowIfFailed(ipabuyer_core_queue_remove(handle, bundleIdPtr));
            }
            finally
            {
                FreeUtf8(bundleIdPtr);
            }
        }

        public static void QueueStart(IntPtr handle)
        {
            ThrowIfFailed(ipabuyer_core_queue_start(handle));
        }

        public static CoreQueueStatus QueueStatus(IntPtr handle)
        {
            ThrowIfFailed(ipabuyer_core_queue_status(handle, out IntPtr json));
            return ReadAndFreeJson(json, CoreJsonContext.Default.CoreQueueStatus);
        }

        public static void QueueCancel(IntPtr handle)
        {
            ThrowIfFailed(ipabuyer_core_queue_cancel(handle));
        }

        public static void QueueDestroy(IntPtr handle)
        {
            ThrowIfFailed(ipabuyer_core_queue_destroy(handle));
        }
    }
}
