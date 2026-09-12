using IPAbuyer.Core.Integration.Ipatool;
using IPAbuyer.Core.Logging;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace IPAbuyer.Core.Services.Purchases
{
    /// <summary>
    /// 通过 ipatool list-purchases 全量同步账户的已拥有 App，并统一标记为已购买。
    /// list-purchases 消耗较大（每 100 个 App 一页请求），刷新时机遵循：
    /// 登录成功且从未同步时、启动且距上次成功同步超过阈值时、用户手动刷新时。
    /// </summary>
    public sealed class PurchaseSyncService
    {
        private static readonly ResourceLoader Loader = new();

        /// <summary>自动同步的最小间隔；低于该间隔的启动触发会被跳过。</summary>
        public static readonly TimeSpan AutoSyncInterval = TimeSpan.FromDays(7);

        /// <summary>list-purchases 单页数量上限（受 ipatool 限制不得超过 100）。</summary>
        public const int PageSize = 100;

        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private bool _isRunning;

        public static PurchaseSyncService Instance { get; } = new();

        public bool IsRunning => _isRunning;

        /// <summary>同步进度回调：(已同步数量, 总数量)。</summary>
        public event Action<int, int>? ProgressChanged;

        /// <summary>判断账户是否需要自动同步（从未同步或超过阈值）。</summary>
        public bool ShouldAutoSync(string account)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                return false;
            }

            DateTime? lastSyncUtc = PurchaseHistoryService.GetLastSuccessfulSyncUtc(account);
            return lastSyncUtc == null || DateTime.UtcNow - lastSyncUtc.Value >= AutoSyncInterval;
        }

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

            await _syncLock.WaitAsync().ConfigureAwait(false);
            bool started = false;
            try
            {
                if (_isRunning)
                {
                    EmitLog(L("PurchaseSync/Log/AlreadyRunning"), UiLogLevel.Tip);
                    return false;
                }

                _isRunning = true;
                started = true;
            }
            finally
            {
                if (!started)
                {
                    _syncLock.Release();
                }
            }

            try
            {
                EmitLog(LF("PurchaseSync/Log/Start", normalizedAccount), UiLogLevel.Info);
                int page = 1;
                int synced = 0;
                int total = -1;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IpatoolResult result = await IpatoolClient.ListPurchasesAsync(
                        PageSize,
                        page,
                        cancellationToken).ConfigureAwait(false);

                    OwnedAppsPage parsed = OwnedAppsPageParser.Parse(result.OutputOrError);
                    if (!parsed.Success)
                    {
                        string message = parsed.ErrorMessage ?? L("PurchaseSync/Error/EmptyResponse");
                        throw new InvalidOperationException(message);
                    }

                    if (total < 0)
                    {
                        total = parsed.TotalCount;
                    }

                    if (parsed.BundleIds.Count > 0)
                    {
                        PurchaseHistoryService.BulkMarkPurchased(parsed.BundleIds, normalizedAccount);
                        synced += parsed.BundleIds.Count;
                        ProgressChanged?.Invoke(synced, Math.Max(total, synced));
                    }

                    bool hasMorePages = synced < total && parsed.BundleIds.Count > 0;
                    if (!hasMorePages)
                    {
                        break;
                    }

                    page++;
                }

                PurchaseHistoryService.RecordSyncAttempt(normalizedAccount, true);
                EmitLog(LF("PurchaseSync/Log/Completed", synced, Math.Max(total, 0)), UiLogLevel.Success);
                return true;
            }
            catch (OperationCanceledException)
            {
                PurchaseHistoryService.RecordSyncAttempt(normalizedAccount, false);
                EmitLog(L("PurchaseSync/Log/Canceled"), UiLogLevel.Tip);
                return false;
            }
            catch (Exception ex)
            {
                PurchaseHistoryService.RecordSyncAttempt(normalizedAccount, false);
                EmitLog(LF("PurchaseSync/Log/Failed", ex.Message), UiLogLevel.Error);
                return false;
            }
            finally
            {
                _isRunning = false;
                _syncLock.Release();
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
