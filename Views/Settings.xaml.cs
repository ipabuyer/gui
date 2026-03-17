using System;
using System.Diagnostics;
using System.Threading.Tasks;
using IPAbuyer.Common;
using IPAbuyer.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace IPAbuyer.Views
{
    public sealed partial class Settings : Page
    {
        public Settings()
        {
            InitializeComponent();
            InitializeCountryCode();
            InitializeDownloadDirectory();
        }

        private void GithubButton(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ipa.blazesnow.com/",
                UseShellExecute = true
            });
        }

        private async void DeleteDataBase(object sender, RoutedEventArgs e)
        {
            int totalBefore = PurchasedAppDb.GetTotalCount();
            var dialog = new ContentDialog
            {
                Title = "确认操作",
                Content = $"确定要清空本地数据库中的已购买记录吗？{Environment.NewLine}当前记录数：{totalBefore} 条。{Environment.NewLine}此操作不可恢复。",
                PrimaryButtonText = "确认清空",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            try
            {
                PurchasedAppDb.ClearPurchasedApps();
                int totalAfter = PurchasedAppDb.GetTotalCount();
                await ShowDialogAsync("操作成功", $"本地记录已清空。{Environment.NewLine}清空前：{totalBefore} 条，清空后：{totalAfter} 条。");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("错误", $"清空失败：{ex.Message}");
            }
        }

        private void InitializeCountryCode()
        {
            try
            {
                string? account = GetAccountForCountryCode();
                string currentCode = KeychainConfig.GetCountryCode(account);
                if (CountryCodeTextBoxControl != null)
                {
                    CountryCodeTextBoxControl.Text = currentCode;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化国家/地区代码失败: {ex.Message}");
            }
        }

        private void InitializeDownloadDirectory()
        {
            try
            {
                if (DownloadDirectoryTextBoxControl != null)
                {
                    DownloadDirectoryTextBoxControl.Text = KeychainConfig.GetDownloadDirectory();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化下载目录失败: {ex.Message}");
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
            if (CountryCodeTextBoxControl == null)
            {
                return;
            }

            string rawInput = CountryCodeTextBoxControl.Text?.Trim() ?? string.Empty;
            bool inputWasEmpty = string.IsNullOrWhiteSpace(rawInput);
            string normalizedInput = inputWasEmpty ? "cn" : rawInput;

            if (!IsValidCountryCode(normalizedInput))
            {
                await ShowDialogAsync("操作失败", "请输入合法的 ISO 3166-1 Alpha-2 国家/地区代码（2 位英文字符）");
                return;
            }

            string normalized = normalizedInput.ToLowerInvariant();
            string? account = GetAccountForCountryCode();

            try
            {
                KeychainConfig.SaveCountryCode(normalized, account);
                CountryCodeTextBoxControl.Text = normalized;

                string message = inputWasEmpty
                    ? "国家/地区代码为空，已恢复为默认值 cn"
                    : $"国家/地区代码已更新为 {normalized}";

                await ShowDialogAsync("操作成功", message);
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("操作失败", $"保存失败：{ex.Message}");
            }
        }

        private static bool IsValidCountryCode(string code)
        {
            return KeychainConfig.IsValidCountryCode(code);
        }

        private async void SaveDownloadDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            string directory = DownloadDirectoryTextBoxControl?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(directory))
            {
                await ShowDialogAsync("操作失败", "下载目录不能为空");
                return;
            }

            try
            {
                KeychainConfig.SaveDownloadDirectory(directory);
                if (DownloadDirectoryTextBoxControl != null)
                {
                    DownloadDirectoryTextBoxControl.Text = KeychainConfig.GetDownloadDirectory();
                }

                await ShowDialogAsync("操作成功", "下载目录已更新");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("操作失败", $"保存失败：{ex.Message}");
            }
        }

        private async void ResetDownloadDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string defaultDirectory = KeychainConfig.GetDefaultDownloadDirectory();
                KeychainConfig.SaveDownloadDirectory(defaultDirectory);
                if (DownloadDirectoryTextBoxControl != null)
                {
                    DownloadDirectoryTextBoxControl.Text = defaultDirectory;
                }

                await ShowDialogAsync("操作成功", $"已恢复默认下载目录：{defaultDirectory}");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("操作失败", $"恢复默认目录失败：{ex.Message}");
            }
        }

        private async void CopyFeedbackEmailButton_Click(object sender, RoutedEventArgs e)
        {
            const string feedbackEmail = "ipa@blazesnow.com";
            try
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(feedbackEmail);
                Clipboard.SetContent(dataPackage);
                Clipboard.Flush();
                await ShowDialogAsync("操作成功", $"反馈邮箱已复制：{feedbackEmail}");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("操作失败", $"复制邮箱失败：{ex.Message}");
            }
        }

        private async Task ShowDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }

        private TextBox? CountryCodeTextBoxControl => FindName("CountryCodeTextBox") as TextBox;
        private TextBox? DownloadDirectoryTextBoxControl => FindName("DownloadDirectoryTextBox") as TextBox;

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
