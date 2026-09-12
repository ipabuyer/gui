using IPAbuyer.Core.Configuration;
using IPAbuyer.Core.Data.PurchasedApps;
using IPAbuyer.Core.Integration.Ipatool;
using IPAbuyer.Core.Logging;
using IPAbuyer.Core.Native;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace IPAbuyer.Core.Services.Purchases
{
    /// <summary>
    /// 通过 ipatool list-purchases 全量同步账户的已拥有 App，并统一标记为已购买。
    /// list-purchases 消耗较大（每 100 个 App 一页请求），仅在设置页由用户手动触发。
    /// 分页拉取、逐页写入与进度/日志上报在 Rust core（轮询句柄）；
    /// 这里以 200ms 间隔轮询 sync_status，把进度与日志转交宿主。
    /// </summary>
    public sealed class PurchaseSyncService
    {
        private static readonly ResourceLoader Loader = new();

        /// <summary>list-purchases 单页数量上限（受 ipatool 限制不得超过 100；Core 侧常量一致）。</summary>
        public const int PageSize = 100;

        private const int PollIntervalMilliseconds = 200;

        private readonly object _syncLock = new();
        private IntPtr _handle = IntPtr.Zero;
        private bool _isRunning;

        public static PurchaseSyncService Instance { get; } = new();

        public bool IsRunning => Volatile.Read(ref _isRunning);

        /// <summary>同步进度回调：(已同步数量, 总数量)。</summary>
        public event Action<int, int>? ProgressChanged;

        /// <summary>
        /// 全量同步账户已拥有 App 并标记为已购买。逐页写入，中断时已拉取的页保留。
        /// </summary>
        public async Task<bool> SyncAsync(string account, CancellationToken cancellationToken = default)
        {
            string normalizedAccount = account?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedAccount))
            {
                return false;
            }

            lock (_syncLock)
            {
                if (_isRunning)
                {
                    EmitLog(L("PurchaseSync/Log/AlreadyRunning"), UiLogLevel.Tip);
                    return false;
                }

                _isRunning = true;
            }

            try
            {
                return await RunSyncAsync(normalizedAccount, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _isRunning, false);
            }
        }

        /// <summary>应用关停：请求 Core 取消在跑的同步（尽力而为，不等待）。</summary>
        public void CancelActive()
        {
            IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    CoreNative.SyncCancel(handle);
                }
                catch
                {
                    // 关停路径：取消失败无需处理。
                }

                // 句柄交还：同步流程仍需 destroy 释放并等待线程结束。
                Interlocked.Exchange(ref _handle, handle);
            }
        }

        private async Task<bool> RunSyncAsync(string normalizedAccount, CancellationToken cancellationToken)
        {
            string dbPath = PurchasedAppDb.GetDatabasePath();
            string executablePath = IpatoolPathResolver.ResolveExecutablePath();
            string passphrase = IpatoolClient.ResolvePassphrase(null);
            bool detailedLog = ApplicationSettings.GetDetailedIpatoolLogEnabled();

            int emittedLogs = 0;
            long lastSynced = -1;
            long lastTotal = -1;

            try
            {
                _handle = await Task.Run(() => CoreNative.SyncCreate(dbPath, executablePath, passphrase, normalizedAccount, detailedLog))
                    .ConfigureAwait(false);
                IntPtr handle = _handle;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    CoreSyncStatus status = await Task.Run(() => CoreNative.SyncStatus(handle)).ConfigureAwait(false);
                    DrainLogs(status.Logs, ref emittedLogs);

                    if (status.Progress.Synced != lastSynced || status.Progress.Total != lastTotal)
                    {
                        lastSynced = status.Progress.Synced;
                        lastTotal = status.Progress.Total;
                        ProgressChanged?.Invoke((int)Math.Max(lastSynced, 0), (int)Math.Max(lastTotal, lastSynced));
                    }

                    if (!status.Running)
                    {
                        bool completed = string.Equals(status.Outcome?.Kind, "completed", StringComparison.Ordinal);
                        // Core 侧部分失败路径（如数据库打开失败）只写 outcome 不发日志，这里兜底展示原因。
                        if (!completed && !string.IsNullOrWhiteSpace(status.Outcome?.Message))
                        {
                            EmitLog(LF("PurchaseSync/Log/Failed", status.Outcome.Message), UiLogLevel.Error);
                        }

                        return completed;
                    }

                    await Task.Delay(PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                CancelHandleQuietly();
                EmitLog(L("PurchaseSync/Log/Canceled"), UiLogLevel.Tip);
                return false;
            }
            catch (Exception ex)
            {
                EmitLog(LF("PurchaseSync/Log/Failed", ex.Message), UiLogLevel.Error);
                return false;
            }
            finally
            {
                DestroyHandleQuietly();
            }
        }

        private void DrainLogs(List<CoreLogEntry> logs, ref int emittedCount)
        {
            for (int i = emittedCount; i < logs.Count; i++)
            {
                CoreLogEntry entry = logs[i];
                EmitLog(CoreMessages.Render(entry.Message), CoreMessages.ToUiLogLevel(entry.Level));
            }

            emittedCount = logs.Count;
        }

        private void CancelHandleQuietly()
        {
            IntPtr handle = _handle;
            if (handle != IntPtr.Zero)
            {
                try
                {
                    CoreNative.SyncCancel(handle);
                }
                catch
                {
                    // 取消路径：Core 已在收尾时自行终止子进程。
                }
            }
        }

        private void DestroyHandleQuietly()
        {
            IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    // destroy 会请求取消并等待 Core 工作线程结束（含子进程终止）。
                    CoreNative.SyncDestroy(handle);
                }
                catch
                {
                    // 句柄释放失败无法恢复。
                }
            }
        }

        private void EmitLog(string message, UiLogLevel level)
        {
            UiLogStore.Append(message, level);
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private static string LF(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key), args);
        }
    }
}
