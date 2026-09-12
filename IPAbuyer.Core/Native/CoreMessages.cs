using IPAbuyer.Core.Logging;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace IPAbuyer.Core.Native
{
    /// <summary>Core 消息（键名 + 参数 / 原文）与日志级别到宿主本地化形式的渲染。</summary>
    internal static class CoreMessages
    {
        private static readonly ResourceLoader Loader = new();

        /// <summary>渲染 Core 消息：键名经 .resw 本地化（缺失键回退为键名），原文直接透传。</summary>
        public static string Render(CoreMessage message)
        {
            if (message == null)
            {
                return string.Empty;
            }

            if (string.Equals(message.Kind, "key", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(message.Key))
            {
                string format = Loader.GetString(message.Key);
                if (string.IsNullOrEmpty(format))
                {
                    return message.Key;
                }

                return message.Args is { Count: > 0 }
                    ? string.Format(CultureInfo.CurrentCulture, format, message.Args.Cast<object>().ToArray())
                    : format;
            }

            return message.Text ?? string.Empty;
        }

        /// <summary>渲染"可能是 resw 键、也可能是原文"的字符串（如队列条目的 last_message）。</summary>
        public static string RenderKeyOrRaw(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string localized = Loader.GetString(text);
            return string.IsNullOrEmpty(localized) ? text : localized;
        }

        public static UiLogLevel ToUiLogLevel(string level)
        {
            return level switch
            {
                "tip" => UiLogLevel.Tip,
                "success" => UiLogLevel.Success,
                "error" => UiLogLevel.Error,
                "ipatool" => UiLogLevel.Ipatool,
                _ => UiLogLevel.Info
            };
        }
    }
}
