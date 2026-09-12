using IPAbuyer.Core.Serialization;
using System.Text.RegularExpressions;

namespace IPAbuyer.Core.Integration.Ipatool
{
    /// <summary>
    /// ipatool 输出的轻量判定：对 payload 字符串做成功/失败/邮箱/keyring 缺失识别。
    /// 命令执行与响应归一化已移入 Rust core；这四个判定保留在宿主侧，
    /// 因为调用方以 payload 字符串为契约（配合 <see cref="IpatoolClient"/> 的静态封装）。
    /// </summary>
    internal static class IpatoolResponseParser
    {
        private static readonly Regex EmailRegex = new(
            @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static string ExtractEmail(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return string.Empty;
            }

            foreach (var token in JsonPayload.EnumerateTokens(payload))
            {
                if (JsonPayload.TryReadString(token, out string? email, "email", "eamil") && !string.IsNullOrWhiteSpace(email))
                {
                    return email.Trim();
                }
            }

            Match match = EmailRegex.Match(payload);
            return match.Success ? match.Value : string.Empty;
        }

        internal static bool IsSuccess(string? payload)
        {
            return JsonPayload.EnumerateTokens(payload).Any(token => JsonPayload.TryReadBoolean(token, "success", out bool success) && success)
                || (!string.IsNullOrWhiteSpace(payload)
                    && (payload.Contains("success=true", StringComparison.OrdinalIgnoreCase)
                        || payload.Contains("\"success\":true", StringComparison.OrdinalIgnoreCase)));
        }

        internal static bool HasExplicitFailure(string? payload)
        {
            return JsonPayload.EnumerateTokens(payload).Any(token => JsonPayload.TryReadBoolean(token, "success", out bool success) && !success)
                || (!string.IsNullOrWhiteSpace(payload)
                    && (payload.Contains("success=false", StringComparison.OrdinalIgnoreCase)
                        || payload.Contains("\"success\":false", StringComparison.OrdinalIgnoreCase)));
        }

        internal static bool IsAccountMissingFromKeyring(string? payload)
        {
            return !string.IsNullOrWhiteSpace(payload)
                && payload.Contains("failed to get account", StringComparison.OrdinalIgnoreCase)
                && payload.Contains("could not be found in the keyring", StringComparison.OrdinalIgnoreCase);
        }
    }
}
