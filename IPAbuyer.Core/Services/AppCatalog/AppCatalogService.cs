using IPAbuyer.Core.Configuration;
using IPAbuyer.Core.Models;
using IPAbuyer.Core.Native;
using IPAbuyer.Core.Services.Purchases;

namespace IPAbuyer.Core.Services.AppCatalog
{
    /// <summary>
    /// App Store 搜索门面：搜索、解析与已购买状态合成在 Rust core
    /// （ipabuyer_core_catalog_search）。Core 返回规范值（price=free/价格、
    /// purchased=purchased/not_purchased/blocked），这里映射为页面绑定的
    /// 本地化显示串（对齐原 AppStoreSearchResponseParser 的输出契约）。
    /// </summary>
    public static class AppCatalogService
    {
        public static async Task<IReadOnlyList<SearchResult>?> SearchAsync(string appName, int limit, string account, CancellationToken cancellationToken)
        {
            string countryCode = NormalizeCountryCode(ApplicationSettings.GetCountryCode());

            List<CorePurchasedAppEntry>? purchasedApps = null;
            if (!string.IsNullOrWhiteSpace(account))
            {
                purchasedApps = PurchaseHistoryService.GetForAccount(account)
                    .Where(record => !string.IsNullOrWhiteSpace(record.AppId))
                    .Select(record => new CorePurchasedAppEntry { AppId = record.AppId, Status = record.Status })
                    .ToList();
            }

            try
            {
                List<CoreSearchResult> items = await Task.Run(() =>
                {
                    using var cancel = new CoreCancelFlag();
                    using CancellationTokenRegistration registration = cancellationToken.Register(cancel.Cancel);
                    return CoreNative.CatalogSearch(appName, limit, countryCode, purchasedApps);
                }).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                return items.Select(MapToSearchResult).ToList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (CoreException)
            {
                // 超时/空响应/响应无效：对齐原实现的 null 语义（页面提示搜索失败）。
                return null;
            }
        }

        public static string NormalizeCountryCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return "cn";
            }

            string normalized = code.Trim().ToLowerInvariant();
            return ApplicationSettings.IsValidCountryCode(normalized) ? normalized : "cn";
        }

        private static SearchResult MapToSearchResult(CoreSearchResult item)
        {
            return new SearchResult
            {
                bundleId = item.BundleId,
                id = item.Id,
                name = item.Name,
                developer = item.Developer,
                artworkUrl = item.ArtworkUrl,
                price = PurchaseStatusPolicy.NormalizePriceForDisplay(item.Price),
                version = item.Version,
                purchased = item.Purchased switch
                {
                    "purchased" => PurchaseStatusPolicy.PurchasedStatus,
                    "blocked" => PurchaseStatusPolicy.PurchaseBlockedStatus,
                    _ => PurchaseStatusPolicy.CanPurchaseStatus,
                },
            };
        }
    }
}
