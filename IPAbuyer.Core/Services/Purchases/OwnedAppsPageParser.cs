using IPAbuyer.Core.Serialization;
using System.Globalization;
using System.Text.Json;

namespace IPAbuyer.Core.Services.Purchases
{
    public sealed record OwnedAppsPage(
        bool Success,
        int TotalCount,
        IReadOnlyList<string> BundleIds,
        string? ErrorMessage = null);

    /// <summary>
    /// 解析 ipatool list-purchases --format json 的单页输出。
    /// 成功形如 {"level":"info","count":5,"totalCount":1753,"page":1,"apps":[{"bundleID":"..."},...]}；
    /// 失败形如 {"level":"error","error":"...","success":false}。
    /// </summary>
    public static class OwnedAppsPageParser
    {
        public static OwnedAppsPage Parse(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new OwnedAppsPage(false, 0, Array.Empty<string>(), payload);
            }

            foreach (JsonElement token in JsonPayload.EnumerateTokens(payload))
            {
                if (IsErrorToken(token))
                {
                    JsonPayload.TryReadString(token, out string? error, "error", "message");
                    return new OwnedAppsPage(false, 0, Array.Empty<string>(), error ?? payload);
                }

                if (!IsSuccessToken(token))
                {
                    continue;
                }

                int totalCount = ReadInt(token, "totalCount");
                List<string> bundleIds = ReadBundleIds(token);
                return new OwnedAppsPage(true, totalCount, bundleIds);
            }

            return new OwnedAppsPage(false, 0, Array.Empty<string>(), payload);
        }

        private static bool IsErrorToken(JsonElement token)
        {
            if (JsonPayload.TryReadString(token, out string? level, "level"))
            {
                return string.Equals(level, "error", StringComparison.OrdinalIgnoreCase);
            }

            return JsonPayload.TryReadBoolean(token, "success", out bool success) && !success;
        }

        private static bool IsSuccessToken(JsonElement token)
        {
            if (token.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (JsonPayload.TryReadString(token, out string? level, "level"))
            {
                return string.Equals(level, "info", StringComparison.OrdinalIgnoreCase)
                    && JsonPayload.TryGetProperty(token, "apps", out JsonElement apps)
                    && apps.ValueKind == JsonValueKind.Array;
            }

            return JsonPayload.TryGetProperty(token, "apps", out JsonElement appsToken)
                && appsToken.ValueKind == JsonValueKind.Array;
        }

        private static int ReadInt(JsonElement token, string name)
        {
            if (!JsonPayload.TryGetProperty(token, name, out JsonElement child))
            {
                return 0;
            }

            return child.ValueKind == JsonValueKind.Number && child.TryGetInt32(out int value)
                ? value
                : int.TryParse(JsonPayload.ReadScalarAsString(child), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : 0;
        }

        private static List<string> ReadBundleIds(JsonElement token)
        {
            var bundleIds = new List<string>();
            if (!JsonPayload.TryGetProperty(token, "apps", out JsonElement apps) || apps.ValueKind != JsonValueKind.Array)
            {
                return bundleIds;
            }

            foreach (JsonElement app in apps.EnumerateArray())
            {
                if (JsonPayload.TryReadString(app, out string? bundleId, "bundleID", "bundleId")
                    && !string.IsNullOrWhiteSpace(bundleId))
                {
                    bundleIds.Add(bundleId.Trim());
                }
            }

            return bundleIds;
        }
    }
}
