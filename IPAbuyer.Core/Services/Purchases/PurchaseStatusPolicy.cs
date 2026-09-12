using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace IPAbuyer.Core.Services.Purchases
{
    public static class PurchaseStatusPolicy
    {
        private static readonly ResourceLoader Loader = new();
        private static readonly string Purchased = L("Common/Status/Purchased");
        private static readonly string CanPurchase = L("Common/Status/NotPurchased");
        private static readonly string PurchaseBlocked = L("Common/Status/PurchaseBlocked");
        private static readonly string Free = L("Common/Price/Free");

        public static string PurchasedStatus => Purchased;
        public static string CanPurchaseStatus => CanPurchase;
        public static string PurchaseBlockedStatus => PurchaseBlocked;

        public static bool IsPurchased(string? status) => Matches(status, Purchased);

        public static bool IsPurchaseBlocked(string? status) => Matches(status, PurchaseBlocked);

        public static bool IsCanPurchase(string? status)
        {
            return string.IsNullOrWhiteSpace(status)
                || IsPurchaseBlocked(status)
                || Matches(status, CanPurchase)
                || Matches(status, L("Common/Status/CanPurchase"));
        }

        public static string NormalizeStoredStatus(string? status)
        {
            // owned 已并入 purchased（数据库 v2 迁移 + 归一化兜底）。
            return PurchaseRecordStatus.TryNormalize(status, out _) ? Purchased : CanPurchase;
        }

        public static string ResolveSearchStatus(string bundleId, string price, IReadOnlyDictionary<string, string> purchasedApps)
        {
            if (!string.IsNullOrWhiteSpace(bundleId) && purchasedApps.TryGetValue(bundleId, out string? status))
            {
                return NormalizeStoredStatus(status);
            }

            return string.IsNullOrWhiteSpace(price) || IsPriceFreeForSearch(price)
                ? CanPurchase
                : PurchaseBlocked;
        }

        public static string ResolveUnpurchasedStatus(string? price)
        {
            return string.IsNullOrWhiteSpace(price) || IsPriceFreeForSearch(price)
                ? CanPurchase
                : PurchaseBlocked;
        }

        public static bool IsPriceFreeForPurchase(string? price)
        {
            if (string.IsNullOrWhiteSpace(price))
            {
                return false;
            }

            return decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
                ? value <= 0m
                : price.Trim().Equals("free", StringComparison.OrdinalIgnoreCase)
                    || price.Trim().Equals(Free, StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizePriceForDisplay(string? rawPrice)
        {
            if (string.IsNullOrWhiteSpace(rawPrice))
            {
                return string.Empty;
            }

            if (decimal.TryParse(rawPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) && value <= 0m)
            {
                return Free;
            }

            return rawPrice.Trim().Equals("free", StringComparison.OrdinalIgnoreCase) ? Free : rawPrice.Trim();
        }

        private static bool IsPriceFreeForSearch(string price)
        {
            return string.IsNullOrWhiteSpace(price) || IsPriceFreeForPurchase(price);
        }

        private static bool Matches(string? actual, string expected)
        {
            return !string.IsNullOrWhiteSpace(actual)
                && actual.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string L(string key) => Loader.GetString(key);
    }
}
