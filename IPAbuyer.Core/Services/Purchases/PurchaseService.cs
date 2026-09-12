using IPAbuyer.Core.Configuration;
using IPAbuyer.Core.Integration.Ipatool;
using IPAbuyer.Core.Models;
using IPAbuyer.Core.Native;
using IPAbuyer.Core.State;

namespace IPAbuyer.Core.Services.Purchases
{
    public enum PurchaseOutcome
    {
        Skipped,
        Purchased,
        AlreadyOwned,
        NeedsOwnedConfirmation,
        Failed
    }

    public sealed record PurchaseResult(
        string BundleId,
        PurchaseOutcome Outcome,
        string? Detail = null);

    /// <summary>
    /// 购买门面：前置策略（已购买跳过、仅免费 App、模拟账户直通）保留在宿主侧，
    /// purchase 命令执行与响应解释在 Rust core（ipabuyer_core_purchase）。
    /// </summary>
    public static class PurchaseService
    {
        public static async Task<PurchaseResult> PurchaseAsync(SearchResult app, string account, CancellationToken cancellationToken)
        {
            string bundleId = app.bundleId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(bundleId)
                || PurchaseStatusPolicy.IsPurchased(app.purchased))
            {
                return new PurchaseResult(bundleId, PurchaseOutcome.Skipped);
            }

            if (!PurchaseStatusPolicy.IsPriceFreeForPurchase(app.price))
            {
                return new PurchaseResult(bundleId, PurchaseOutcome.Skipped, "NonFree");
            }

            bool isMockAccount = SessionState.IsLoggedIn
                && SessionState.IsMockAccount
                && string.Equals(SessionState.CurrentAccount, account, StringComparison.OrdinalIgnoreCase);
            if (isMockAccount)
            {
                PurchaseHistoryService.Mark(bundleId, account, PurchaseStatusPolicy.PurchasedStatus);
                return new PurchaseResult(bundleId, PurchaseOutcome.Purchased, "Mock");
            }

            string executablePath = IpatoolPathResolver.ResolveExecutablePath();
            string passphrase = IpatoolClient.ResolvePassphrase(null);
            bool detailedLog = ApplicationSettings.GetDetailedIpatoolLogEnabled();

            CorePurchaseResult response = await Task.Run(() =>
            {
                using var cancel = new CoreCancelFlag();
                using CancellationTokenRegistration registration = cancellationToken.Register(cancel.Cancel);
                return CoreNative.Purchase(executablePath, bundleId, passphrase, detailedLog, cancel.Handle);
            }).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            IpatoolClient.EmitCoreLogs(response.Logs);

            PurchaseOutcome outcome = response.Outcome switch
            {
                "Purchased" => PurchaseOutcome.Purchased,
                "AlreadyOwned" => PurchaseOutcome.AlreadyOwned,
                "NeedsOwnedConfirmation" => PurchaseOutcome.NeedsOwnedConfirmation,
                _ => PurchaseOutcome.Failed,
            };

            // 已拥有已合并为已购买：成功、alreadyOwned、STDQ 三种结果都写入同一条已购买记录。
            if (outcome == PurchaseOutcome.Purchased
                || outcome == PurchaseOutcome.AlreadyOwned
                || outcome == PurchaseOutcome.NeedsOwnedConfirmation)
            {
                PurchaseHistoryService.Mark(bundleId, account, PurchaseStatusPolicy.PurchasedStatus);
            }

            return new PurchaseResult(bundleId, outcome, response.RawPayload);
        }

        public static void Mark(string bundleId, string account, string status)
        {
            PurchaseHistoryService.Mark(bundleId, account, status);
        }

        public static void RemoveMark(string bundleId, string account)
        {
            PurchaseHistoryService.RemoveMark(bundleId, account);
        }
    }
}
