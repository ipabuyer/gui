namespace IPAbuyer.Core.Services.Purchases
{
    internal static class PurchaseRecordStatus
    {
        internal const string Purchased = "purchased";

        internal static bool TryNormalize(string? status, out string normalizedStatus)
        {
            normalizedStatus = string.Empty;
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            string value = status.Trim();

            // “已拥有”已合并进“已购买”：历史 owned 值在此归一化为 purchased。
            if (value.Equals(Purchased, StringComparison.OrdinalIgnoreCase)
                || value.Equals("已购买", StringComparison.Ordinal)
                || value.Equals("owned", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Already owned", StringComparison.OrdinalIgnoreCase)
                || value.Equals("已拥有", StringComparison.Ordinal))
            {
                normalizedStatus = Purchased;
                return true;
            }

            return false;
        }
    }
}
