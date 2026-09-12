using IPAbuyer.Core.Configuration;
using IPAbuyer.Core.Integration.Ipatool;
using IPAbuyer.Core.Logging;
using IPAbuyer.Core.Models;
using IPAbuyer.Core.Native;
using IPAbuyer.Core.State;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using System.Globalization;

namespace IPAbuyer.Core.Services.Downloads
{
    /// <summary>宿主侧入队结果枚举（成员含义与页面既有用法一致）。</summary>
    public enum DownloadQueueAddResult
    {
        Ignored = 0,
        Added = 1,
        Updated = 2,
        Requeued = 3
    }

    /// <summary>
    /// 下载队列门面：串行状态机、输出解析与进程编排都在 Rust core
    /// （ipabuyer_core_queue_* 轮询句柄）。这里维护 ObservableCollection 镜像、
    /// 以 200ms 轮询 queue_status 同步条目状态与日志，并保留页面既有的事件 API。
    /// 注意：Core 的取消是队列级——"终止下载"会终止当前下载并结束本轮队列，
    /// 剩余条目保留原状态，可再次启动继续。
    /// </summary>
    public sealed class DownloadQueueService
    {
        private static readonly ResourceLoader Loader = new();

        private const int PollIntervalMilliseconds = 200;

        private readonly object _handleLock = new();
        private IntPtr _handle = IntPtr.Zero;
        private string _handleFingerprint = string.Empty;
        private int _isRunning;

        // Core 的 queue_status 返回累积日志；此游标记录已转交给 LogReceived 的条数，
        // 由入队/移除与轮询两条路径共享，避免重复上抛。
        private int _emittedLogCount;

        private DownloadQueueService()
        {
        }

        public static DownloadQueueService Instance { get; } = new();

        public ObservableCollection<DownloadQueueItem> Items { get; } = new();

        public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

        public event Action<UiLogMessage>? LogReceived;

        public event Action? QueueChanged;

        public DownloadQueueAddResult AddOrUpdateFromSearchResult(SearchResult app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.bundleId))
            {
                return DownloadQueueAddResult.Ignored;
            }

            string bundleId = app.bundleId.Trim();
            int coreResult;
            lock (_handleLock)
            {
                EnsureHandleLocked();
                coreResult = CoreNative.QueueAdd(_handle, SerializeQueueItem(app, bundleId));
            }

            DownloadQueueAddResult result = coreResult switch
            {
                0 => DownloadQueueAddResult.Added,
                1 => DownloadQueueAddResult.Updated,
                2 => DownloadQueueAddResult.Requeued,
                _ => DownloadQueueAddResult.Ignored,
            };

            if (result != DownloadQueueAddResult.Ignored)
            {
                MirrorQueueAdd(app, bundleId, result);
            }

            DrainPendingLogs();
            NotifyQueueChanged();
            return result;
        }

        public int RemoveItems(IEnumerable<DownloadQueueItem> items)
        {
            if (items == null)
            {
                return 0;
            }

            int removed = 0;
            foreach (DownloadQueueItem item in items.ToList())
            {
                if (IsRunning && item.Status == DownloadQueueStatus.Downloading)
                {
                    continue;
                }

                lock (_handleLock)
                {
                    if (_handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        CoreNative.QueueRemove(_handle, item.BundleId);
                    }
                    catch (CoreException)
                    {
                        // 条目不在 Core 队列中（尚未镜像或已被移除）：忽略。
                        continue;
                    }
                }

                if (Items.Remove(item))
                {
                    removed++;
                }
            }

            if (removed > 0)
            {
                DrainPendingLogs();
                NotifyQueueChanged();
            }

            return removed;
        }

        public async Task<int> StartQueueAsync()
        {
            // 未登录时不启动（对齐原 ResolveAccount 守卫）。
            string account = SessionState.IsLoggedIn ? SessionState.CurrentAccount : string.Empty;
            if (string.IsNullOrWhiteSpace(account))
            {
                string message = SessionState.IsLoggedIn
                    ? L("DownloadQueue/Error/MissingSessionEmail")
                    : L("DownloadQueue/Error/MissingAccount");
                EmitLog(message, UiLogLevel.Error);
                return 0;
            }

            IntPtr handle;
            lock (_handleLock)
            {
                EnsureHandleLocked();
                handle = _handle;
            }

            if (handle == IntPtr.Zero)
            {
                return 0;
            }

            try
            {
                CoreNative.QueueStart(handle);
            }
            catch (CoreException)
            {
                EmitLog(L("DownloadQueue/Log/AlreadyRunning"), UiLogLevel.Info);
                return 0;
            }

            Volatile.Write(ref _isRunning, 1);
            NotifyQueueChanged();

            try
            {
                string itemsSnapshot = string.Empty;

                while (true)
                {
                    CoreQueueStatus status = await Task.Run(() => CoreNative.QueueStatus(handle)).ConfigureAwait(false);
                    DrainLogs(status.Logs);
                    ApplyItemStatuses(status.Items);

                    Volatile.Write(ref _isRunning, status.Running ? 1 : 0);

                    string snapshot = string.Join("|", status.Items.Select(i => $"{i.BundleId}:{i.Status}:{i.LastMessage}"));
                    if (!string.Equals(snapshot, itemsSnapshot, StringComparison.Ordinal) || !status.Running)
                    {
                        itemsSnapshot = snapshot;
                        NotifyQueueChanged();
                    }

                    if (!status.Running)
                    {
                        return status.Completed ?? 0;
                    }

                    await Task.Delay(PollIntervalMilliseconds).ConfigureAwait(false);
                }
            }
            finally
            {
                Volatile.Write(ref _isRunning, 0);
                NotifyQueueChanged();
            }
        }

        /// <summary>终止当前下载并结束本轮队列（Core 的取消为队列级）。</summary>
        public void CancelCurrent()
        {
            lock (_handleLock)
            {
                if (_handle != IntPtr.Zero)
                {
                    CoreNative.QueueCancel(_handle);
                }
            }

            EmitLog(L("DownloadQueue/Log/CancelCurrentRequested"), UiLogLevel.Tip);
        }

        public void CancelAll()
        {
            lock (_handleLock)
            {
                if (_handle != IntPtr.Zero)
                {
                    CoreNative.QueueCancel(_handle);
                }
            }

            foreach (DownloadQueueItem item in Items.Where(i => i.Status == DownloadQueueStatus.Pending).ToList())
            {
                item.Status = DownloadQueueStatus.Canceled;
                item.LastMessage = L("DownloadQueue/Status/QueueCanceled");
            }

            EmitLog(L("DownloadQueue/Log/CancelAllRequested"), UiLogLevel.Tip);
            NotifyQueueChanged();
        }

        /// <summary>应用关停：请求 Core 取消在跑的队列（尽力而为，不等待）。</summary>
        public void CancelActive()
        {
            lock (_handleLock)
            {
                if (_handle != IntPtr.Zero)
                {
                    try
                    {
                        CoreNative.QueueCancel(_handle);
                    }
                    catch
                    {
                        // 关停路径：取消失败无需处理。
                    }
                }
            }
        }

        // ---- 镜像与句柄管理 ----

        private void MirrorQueueAdd(SearchResult app, string bundleId, DownloadQueueAddResult result)
        {
            lock (Items)
            {
                DownloadQueueItem? existing = Items.FirstOrDefault(i => i.BundleId == bundleId);
                if (existing != null)
                {
                    existing.AppId = app.id ?? existing.AppId;
                    existing.Name = app.name ?? existing.Name;
                    existing.Developer = app.developer ?? existing.Developer;
                    existing.Version = app.version ?? existing.Version;
                    existing.Price = app.price ?? existing.Price;
                    existing.ArtworkUrl = app.artworkUrl ?? existing.ArtworkUrl;

                    if (result == DownloadQueueAddResult.Requeued)
                    {
                        existing.Status = DownloadQueueStatus.Pending;
                        existing.LastMessage = L("DownloadQueue/Status/Requeued");
                    }

                    return;
                }

                Items.Add(new DownloadQueueItem
                {
                    BundleId = bundleId,
                    AppId = app.id ?? string.Empty,
                    Name = app.name ?? bundleId,
                    Developer = app.developer ?? string.Empty,
                    Version = app.version ?? string.Empty,
                    Price = app.price ?? string.Empty,
                    ArtworkUrl = app.artworkUrl ?? string.Empty,
                    Status = DownloadQueueStatus.Pending,
                    LastMessage = L("DownloadQueue/Status/Pending")
                });
            }
        }

        private void ApplyItemStatuses(List<CoreQueueItem> items)
        {
            lock (Items)
            {
                foreach (CoreQueueItem coreItem in items)
                {
                    DownloadQueueItem? mirror = Items.FirstOrDefault(i => string.Equals(i.BundleId, coreItem.BundleId, StringComparison.OrdinalIgnoreCase));
                    if (mirror == null)
                    {
                        continue;
                    }

                    mirror.Status = coreItem.Status switch
                    {
                        "Pending" => DownloadQueueStatus.Pending,
                        "Downloading" => DownloadQueueStatus.Downloading,
                        "Success" => DownloadQueueStatus.Success,
                        "Failed" => DownloadQueueStatus.Failed,
                        "Canceled" => DownloadQueueStatus.Canceled,
                        _ => mirror.Status,
                    };

                    // last_message 为 resw 键名或错误原文，缺失键回退原文。
                    mirror.LastMessage = CoreMessages.RenderKeyOrRaw(coreItem.LastMessage);
                }
            }
        }

        private string CurrentFingerprint()
        {
            return string.Join("\u001F",
                IpatoolPathResolver.ResolveExecutablePath(),
                ApplicationSettings.GetDownloadDirectory(),
                IpatoolClient.ResolvePassphrase(null),
                SessionState.IsLoggedIn && SessionState.IsMockAccount ? "1" : "0",
                ApplicationSettings.GetDetailedIpatoolLogEnabled() ? "1" : "0");
        }

        private void EnsureHandleLocked()
        {
            string fingerprint = CurrentFingerprint();
            if (_handle != IntPtr.Zero && string.Equals(fingerprint, _handleFingerprint, StringComparison.Ordinal))
            {
                return;
            }

            if (_handle != IntPtr.Zero)
            {
                try
                {
                    CoreNative.QueueDestroy(_handle);
                }
                catch
                {
                    // 旧句柄释放失败不阻塞重建。
                }
                _handle = IntPtr.Zero;
            }

            string[] parts = fingerprint.Split('\u001F');
            _handle = CoreNative.QueueCreate(
                parts[0],
                parts[1],
                parts[2],
                parts[3] == "1",
                parts[4] == "1");
            _handleFingerprint = fingerprint;

            // 句柄重建后，把镜像条目重新入队，保持两侧状态一致。
            foreach (DownloadQueueItem item in Items)
            {
                try
                {
                    CoreNative.QueueAdd(_handle, SerializeMirrorItem(item));
                }
                catch (CoreException)
                {
                    // 重建入队失败（如条目无效）：跳过。
                }
            }
        }

        private static string SerializeQueueItem(SearchResult app, string bundleId)
        {
            var payload = new CoreQueueItemInput
            {
                BundleId = bundleId,
                Id = app.id,
                Name = app.name,
                Developer = app.developer,
                ArtworkUrl = app.artworkUrl,
                Price = app.price ?? string.Empty,
                Version = app.version,
            };
            return CoreNative.WriteJson(payload, CoreJsonContext.Default.CoreQueueItemInput);
        }

        private static string SerializeMirrorItem(DownloadQueueItem item)
        {
            var payload = new CoreQueueItemInput
            {
                BundleId = item.BundleId,
                Id = string.IsNullOrEmpty(item.AppId) ? null : item.AppId,
                Name = string.IsNullOrEmpty(item.Name) ? null : item.Name,
                Developer = string.IsNullOrEmpty(item.Developer) ? null : item.Developer,
                ArtworkUrl = string.IsNullOrEmpty(item.ArtworkUrl) ? null : item.ArtworkUrl,
                Price = string.IsNullOrEmpty(item.Price) ? "free" : item.Price,
                Version = string.IsNullOrEmpty(item.Version) ? null : item.Version,
            };
            return CoreNative.WriteJson(payload, CoreJsonContext.Default.CoreQueueItemInput);
        }

        /// <summary>拉取一次队列状态，把未上抛过的累积日志转交 LogReceived（入队/移除后的即时反馈）。</summary>
        private void DrainPendingLogs()
        {
            IntPtr handle = _handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            CoreQueueStatus status;
            try
            {
                status = CoreNative.QueueStatus(handle);
            }
            catch (CoreException)
            {
                return;
            }

            DrainLogs(status.Logs);
        }

        private void DrainLogs(List<CoreLogEntry> logs)
        {
            for (int i = Volatile.Read(ref _emittedLogCount); i < logs.Count; i++)
            {
                CoreLogEntry entry = logs[i];
                string message = CoreMessages.Render(entry.Message);
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                UiLogLevel level = CoreMessages.ToUiLogLevel(entry.Level);
                UiLogSource source = level == UiLogLevel.Ipatool ? UiLogSource.Ipatool : UiLogSource.App;
                LogReceived?.Invoke(new UiLogMessage(message, level, source));
            }

            Volatile.Write(ref _emittedLogCount, logs.Count);
        }

        private void EmitLog(string message, UiLogLevel level)
        {
            LogReceived?.Invoke(new UiLogMessage(message, level, UiLogSource.App));
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private void NotifyQueueChanged()
        {
            QueueChanged?.Invoke();
        }
    }
}
