using IPAbuyer.Core.Data.PurchasedApps;
using System.Globalization;

namespace IPAbuyer.Core.Services.Purchases
{
    public static class PurchaseHistoryService
    {
        public static void Initialize()
        {
            PurchasedAppDb.InitDb();
        }

        public static IReadOnlyList<(string AppId, string Status)> GetForAccount(string account)
        {
            return PurchasedAppDb.GetPurchasedApps(account)
                .Select(record => (record.appID, record.status))
                .ToArray();
        }

        public static void Mark(string appId, string account, string status)
        {
            PurchasedAppDb.SavePurchasedApp(appId, account, status);
        }

        public static void RemoveMark(string appId, string account)
        {
            PurchasedAppDb.RemovePurchasedApp(appId, account);
        }

        public static void Clear()
        {
            PurchasedAppDb.ClearPurchasedApps();
        }

        public static int GetTotalCount()
        {
            return PurchasedAppDb.GetTotalCount();
        }

        public static DateTime? GetLastSuccessfulSyncUtc(string account)
        {
            return PurchasedAppDb.GetLastSuccessfulSyncUtc(account);
        }

        public static void RecordSyncAttempt(string account, bool succeeded)
        {
            PurchasedAppDb.RecordSyncAttempt(account, succeeded, DateTime.UtcNow);
        }

        public static int BulkMarkPurchased(IEnumerable<string> appIds, string account)
        {
            return PurchasedAppDb.BulkMarkPurchased(appIds, account);
        }
    }
}
