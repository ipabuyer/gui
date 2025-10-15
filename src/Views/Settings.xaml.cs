using IPAbuyer.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace IPAbuyer.Views
{
    public sealed partial class Settings : Page
    {

        public Settings()
        {
            this.InitializeComponent();
            InitializeCountryCode();
        }

        private void OpenAppleAccountLink(object sender, RoutedEventArgs e)
        {
            var url = "https://account.apple.com";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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
            var url = "https://github.com/ipabuyer/";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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
                string currentCode = KeychainConfig.GetCountryCode();
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
            var textBox = CountryCodeTextBoxControl;
            if (textBox == null)
            {
                return;
            }

            string input = textBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                await ShowCountryCodeDialogAsync("请输入国家或地区代码", isError: true);
                return;
            }

            if (!IsValidCountryCode(input))
            {
                await ShowCountryCodeDialogAsync("请输入合法的 ISO 3166-1 Alpha-2 国家/地区代码（两位英文字母）", isError: true);
                return;
            }

            string normalized = input.ToLowerInvariant();

            try
            {
                KeychainConfig.SaveCountryCode(normalized);
                textBox.Text = normalized;
                await ShowCountryCodeDialogAsync($"国家/地区代码已更新为 {normalized}", isError: false);
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
    }
}
