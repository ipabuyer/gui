using IPAbuyer.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.System;

namespace IPAbuyer.Views
{
    public sealed partial class Settings : Page
    {

        public Settings()
        {
            this.InitializeComponent();
            InitializeCountryCode();
        }

        private void AppleAccountButton(object sender, RoutedEventArgs e)
        {
            var url = "https://account.apple.com/";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        // 返回首页
        private void BackToMainpage(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        // 明暗模式 ComboBox 改变时触发(已删除)
        // 跳转到开发者官网
        private void GithubButton(object sender, RoutedEventArgs e)
        {
            var url = "https://ipa.blazesnow.com/";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        // 清除本地数据库
        private async void DeleteDataBase(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "确认操作",
                Content = "确定要删除本地所有已购买记录吗？此操作不可恢复！",
                PrimaryButtonText = "确认",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    IPAbuyer.Data.PurchasedAppDb.ClearPurchasedApps();
                    var successDialog = new ContentDialog
                    {
                        Title = "操作成功",
                        Content = "本地已购记录已清除。",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "错误",
                        Content = $"清除失败：{ex.Message}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                }
            }
        }

        private void InitializeCountryCode()
        {
            try
            {
                string? account = GetAccountForCountryCode();
                string currentCode = KeychainConfig.GetCountryCode(account);
                var textBox = CountryCodeTextBoxControl;
                if (textBox != null)
                {
                    textBox.Text = currentCode;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化国家/地区代码失败: {ex.Message}");
            }
        }

        private async void CountryCodeButton(object sender, RoutedEventArgs e)
        {
            await HandleCountryCodeSubmissionAsync();
        }

        private async void CountryCodeTextBox_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            await HandleCountryCodeSubmissionAsync();
        }

        private async Task HandleCountryCodeSubmissionAsync()
        {
            var textBox = CountryCodeTextBoxControl;
            if (textBox == null)
            {
                return;
            }

            string rawInput = textBox.Text?.Trim() ?? string.Empty;
            bool inputWasEmpty = string.IsNullOrWhiteSpace(rawInput);
            string normalizedInput = inputWasEmpty ? "cn" : rawInput;

            if (!IsValidCountryCode(normalizedInput))
            {
                await ShowCountryCodeDialogAsync("请输入合法的 ISO 3166-1 Alpha-2 国家/地区代码（两位英文字母）", isError: true);
                return;
            }

            string normalized = normalizedInput.ToLowerInvariant();
            string? account = GetAccountForCountryCode();

            try
            {
                KeychainConfig.SaveCountryCode(normalized, account);
                textBox.Text = normalized;

                string message;
                if (inputWasEmpty)
                {
                    message = "国家/地区代码为空，已恢复为默认值 cn";
                }
                else if (string.Equals(normalized, "cn", StringComparison.OrdinalIgnoreCase))
                {
                    message = "国家/地区代码已更新为 cn";
                }
                else
                {
                    message = $"国家/地区代码已更新为 {normalized}";
                }

                await ShowCountryCodeDialogAsync(message, isError: false);
            }
            catch (Exception ex)
            {
                await ShowCountryCodeDialogAsync($"保存失败：{ex.Message}", isError: true);
            }
        }

        private static bool IsValidCountryCode(string code)
        {
            return KeychainConfig.IsValidCountryCode(code);
        }

        private async Task ShowCountryCodeDialogAsync(string message, bool isError)
        {
            var dialog = new ContentDialog
            {
                Title = isError ? "操作失败" : "操作成功",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private TextBox? CountryCodeTextBoxControl => FindName("CountryCodeTextBox") as TextBox;

        private static string? GetAccountForCountryCode()
        {
            if (SessionState.IsLoggedIn)
            {
                string account = SessionState.CurrentAccount;
                if (!string.IsNullOrWhiteSpace(account))
                {
                    return account;
                }
            }

            string? lastLogin = KeychainConfig.GetLastLoginUsername();
            return string.IsNullOrWhiteSpace(lastLogin) ? null : lastLogin;
        }
    }
}
