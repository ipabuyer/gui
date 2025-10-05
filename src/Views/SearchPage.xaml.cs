using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Threading.Tasks;

namespace IPAbuyer.Views
{
    public sealed partial class SearchPage : Page
    {
        private const string keychainPassphrase = "12345678";
        public SearchPage()
        {
            this.InitializeComponent();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string appName = AppNameBox.Text.Trim();
            if (string.IsNullOrEmpty(appName))
            {
                ResultText.Text = "请输入APP名称";
                return;
            }
            SearchButton.IsEnabled = false;
            string cmd = $"./ipatool.exe search --keychain-passphrase {keychainPassphrase} {appName} --limit 100 --non-interactive --format json";
            var result = await RunCommandAsync(cmd);
            SearchButton.IsEnabled = true;

            // 解析结果为列表（按逗号分割每个json对象，不限制条数）
            var allResults = new System.Collections.Generic.List<string>();
            int start = result.IndexOf('{');
            while (start >= 0)
            {
                int end = result.IndexOf('}', start);
                if (end > start)
                {
                    string item = result.Substring(start, end - start + 1);
                    allResults.Add(item);
                    start = result.IndexOf('{', end);
                }
                else
                {
                    break;
                }
            }

            // 跳转到结果页面并传递数据
            var frame = this.Frame;
            frame.Navigate(typeof(SearchResultPage));
            // 获取新页面实例并设置结果
            if (frame.Content is SearchResultPage resultPage)
            {
                resultPage.SetResults(allResults);
            }
        }

        private async Task<string> RunCommandAsync(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    return string.IsNullOrEmpty(error) ? output : error;
                }
            }
            catch (Exception ex)
            {
                return $"命令执行失败: {ex.Message}";
            }
        }

        private void PurchasedHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PurchasedListPage));
        }
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            IPAbuyer.Data.AccountHistoryDb.SetLogoutFlag();
            Frame.Navigate(typeof(LoginPage));
        }
    }
}
