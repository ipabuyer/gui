using System;
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
        public UiLogMessage(string message, UiLogLevel level, UiLogSource source)
        {
            Message = message;
            Level = level;
            Source = source;
        }

        public string Message { get; }
        public UiLogLevel Level { get; }
        public UiLogSource Source { get; }
    }

    public static class UiLogFormatter
    {
        public static UiLogEntry Build(string message, UiLogLevel level)
        {
            string raw = message?.Trim() ?? string.Empty;
            string tag = level switch
            {
                UiLogLevel.Tip => "TIP",
                UiLogLevel.Success => "SUCCESS",
                UiLogLevel.Error => "ERROR",
                UiLogLevel.Ipatool => "ipatool",
                _ => "INFO"
            };

            string formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{tag}] {raw}";
            return new UiLogEntry(formatted, level);
        }
    }
}
