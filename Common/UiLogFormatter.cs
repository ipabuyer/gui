using System;
using System.Text.RegularExpressions;

namespace IPAbuyer.Common
{
    public enum UiLogLevel
    {
        Info = 0,
        Tip = 1,
        Success = 2,
        Error = 3,
        Ipatool = 4
    }

    public enum UiLogSource
    {
        Auto = 0,
        App = 1,
        Ipatool = 2
    }

    public readonly struct UiLogEntry
    {
        public UiLogEntry(string formattedText, UiLogLevel level)
        {
            FormattedText = formattedText;
            Level = level;
        }

        public string FormattedText { get; }
        public UiLogLevel Level { get; }
    }

    public readonly struct UiLogMessage
    {
        public UiLogMessage(string message, UiLogSource source)
        {
            Message = message;
            Source = source;
        }

        public string Message { get; }
        public UiLogSource Source { get; }
    }

    public static class UiLogFormatter
    {
        private static readonly Regex TimePrefixRegex = new(@"^\[\d{1,2}:\d{2}:\d{2}\]\s*", RegexOptions.Compiled);
        private static readonly Regex LegacyLevelPrefixRegex = new(@"^\[(提示|错误|成功|验证码错误|验证码提示)\]\s*", RegexOptions.Compiled);

        public static UiLogEntry Build(string message, UiLogSource source = UiLogSource.Auto)
        {
            string raw = message?.Trim() ?? string.Empty;
            string normalized = NormalizeMessage(raw);
            UiLogLevel level = DetectLevel(raw, normalized, source);
            string tag = level switch
            {
                UiLogLevel.Tip => "TIP",
                UiLogLevel.Success => "SUCCESS",
                UiLogLevel.Error => "ERROR",
                UiLogLevel.Ipatool => "ipatool",
                _ => "INFO"
            };

            string formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{tag}] {normalized}";
            return new UiLogEntry(formatted, level);
        }

        private static string NormalizeMessage(string message)
        {
            string normalized = message?.Trim() ?? string.Empty;
            normalized = TimePrefixRegex.Replace(normalized, string.Empty).Trim();
            normalized = LegacyLevelPrefixRegex.Replace(normalized, string.Empty).Trim();
            return normalized;
        }

        private static UiLogLevel DetectLevel(string rawMessage, string normalizedMessage, UiLogSource source)
        {
            if (source == UiLogSource.Ipatool)
            {
                return UiLogLevel.Ipatool;
            }

            if (rawMessage.Contains("[错误]", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("[验证码错误]", StringComparison.OrdinalIgnoreCase)
                || normalizedMessage.Contains(" exception", StringComparison.OrdinalIgnoreCase)
                || normalizedMessage.Contains(" failed", StringComparison.OrdinalIgnoreCase)
                || normalizedMessage.Contains("失败", StringComparison.OrdinalIgnoreCase)
                || normalizedMessage.Contains("错误", StringComparison.OrdinalIgnoreCase)
                || normalizedMessage.Contains("ERR ", StringComparison.OrdinalIgnoreCase))
            {
                return UiLogLevel.Error;
            }

            if (rawMessage.Contains("[提示]", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("[验证码提示]", StringComparison.OrdinalIgnoreCase)
                || normalizedMessage.Contains("提示", StringComparison.OrdinalIgnoreCase))
            {
                return UiLogLevel.Tip;
            }

            if (rawMessage.Contains("[成功]", StringComparison.OrdinalIgnoreCase)
                || normalizedMessage.Contains("成功", StringComparison.OrdinalIgnoreCase))
            {
                return UiLogLevel.Success;
            }

            if (source == UiLogSource.Auto && IsIpatoolMessage(normalizedMessage))
            {
                return UiLogLevel.Ipatool;
            }

            return UiLogLevel.Info;
        }

        private static bool IsIpatoolMessage(string message)
        {
            if (message.Contains("\"level\"", StringComparison.OrdinalIgnoreCase)
                || message.Contains("success=true", StringComparison.OrdinalIgnoreCase)
                || message.Contains("error=", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ipatool", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
