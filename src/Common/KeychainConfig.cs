using System;
using System.Diagnostics;
using Windows.Storage;

namespace IPAbuyer.Common
{
    /// <summary>
    /// 提供 Keychain passphrase 的统一获取/设置逻辑。
    /// 优先级：环境变量 IPABUYER_KEYCHAIN_PASSPHRASE -> 本地设置 -> 默认值
    /// 这样可以避免把敏感字符串硬编码到源码中。
    /// </summary>
    public static class KeychainConfig
    {
        private const string DefaultPassphrase = "12345678";
        private const string SettingKey = "KeychainPassphrase";

        public static string GetPassphrase()
        {
            try
            {
                // 优先使用环境变量（便于 CI / 本地覆盖）
                var env = Environment.GetEnvironmentVariable("IPABUYER_KEYCHAIN_PASSPHRASE");
                if (!string.IsNullOrEmpty(env))
                    return env;

                // 尝试从本地设置读取（可通过设置页面保存）
                var local = ApplicationData.Current?.LocalSettings;
                if (local != null && local.Values.ContainsKey(SettingKey))
                {
                    var val = local.Values[SettingKey] as string;
                    if (!string.IsNullOrEmpty(val))
                        return val;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"KeychainConfig:GetPassphrase 异常: {ex.Message}");
            }

            // 回退到默认值（保持向后兼容）
            Debug.WriteLine("使用默认 Keychain passphrase（建议通过环境变量或设置覆盖此值）。");
            return DefaultPassphrase;
        }

        public static void SetPassphrase(string passphrase)
        {
            try
            {
                var local = ApplicationData.Current?.LocalSettings;
                if (local != null)
                {
                    local.Values[SettingKey] = passphrase;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"KeychainConfig:SetPassphrase 异常: {ex.Message}");
            }
        }
    }
}
