using IPAbuyer.Common;
using IPAbuyer.Data;
using IPAbuyer.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WinRT.Interop;

namespace IPAbuyer
{
    public partial class App : Application
    {
        private Window? _window;
        public Window? MainWindowInstance => _window;

        // 构造函数
        public App()
        {
            try
            {
                // 初始化数据库
                PurchasedAppDb.InitDb();
                // KeychainConfig 改为文件配置（无 KeychainConfig.db），保留初始化入口用于创建默认配置文件。
                KeychainConfig.InitializeDatabase();

                this.InitializeComponent();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动错误: {ex.Message}");
                throw;
            }
        }

        // 应用启动时调用
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                _ = WarmupAuthInfoAsync();
                _window = new MainWindow();
                // 激活窗口
                _window.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动错误: {ex.Message}");
                throw;
            }
        }

        private static async Task WarmupAuthInfoAsync()
        {
            try
            {
                var result = await IpatoolExecution.AuthInfoAsync().ConfigureAwait(false);
                if (!result.IsSuccessResponse)
                {
                    return;
                }

                string account = ExtractEmailFromAuthInfoPayload(result.OutputOrError);
                if (string.IsNullOrWhiteSpace(account))
                {
                    account = KeychainConfig.GetLastLoginUsername() ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(account))
                {
                    return;
                }

                KeychainConfig.GenerateAndSaveSecretKey(account);
                SessionState.SetLoginState(account, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"静默查询登录状态失败: {ex.Message}");
            }
        }

        private static string ExtractEmailFromAuthInfoPayload(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return string.Empty;
            }

            string normalized = payload.Replace("}{", "}\n{");
            string[] lines = normalized.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(trimmed);
                    JsonElement root = document.RootElement;
                    if (TryReadEmailProperty(root, "email", out string email))
                    {
                        return email;
                    }

                    if (TryReadEmailProperty(root, "eamil", out email))
                    {
                        return email;
                    }
                }
                catch (JsonException)
                {
                    // ignore invalid segment
                }
            }

            return string.Empty;
        }

        private static bool TryReadEmailProperty(JsonElement root, string propertyName, out string email)
        {
            if (root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String)
            {
                string? value = element.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    email = value;
                    return true;
                }
            }

            email = string.Empty;
            return false;
        }
    }
}
