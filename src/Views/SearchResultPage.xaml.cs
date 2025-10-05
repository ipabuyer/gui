using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;

namespace IPAbuyer.Views
{
    public sealed partial class SearchResultPage : Page
    {
        async void ResultRow_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.DataContext is AppResult app)
            {
                if (app.price != "0")
                {
                    ShowDialog("购买失败，价格不为0");
                    return;
                }
                string cmd = $"./ipatool.exe purchase --keychain-passphrase 12345678 --bundle-identifier {app.bundleID}";
                string result = await RunCommandAsync(cmd);
                if (result.Contains("success")&&result.Contains("false"))
                {
                    ShowDialog("购买失败，已购买该商品");
                }
                else if ((result.Contains("success") && result.Contains("true")) || result.Contains("购买成功"))
                {
                    ShowDialog("购买成功");
                }
                else
                {
                    ShowDialog("购买失败");
                }
            }
        }

        async Task<string> RunCommandAsync(string command)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    return string.IsNullOrEmpty(error) ? output : error;
                }
            }
            catch (System.Exception ex)
            {
                return $"命令执行失败: {ex.Message}";
            }
        }

        async void ShowDialog(string msg)
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = msg,
                CloseButtonText = "确定"
            };
            dialog.XamlRoot = this.XamlRoot;
            await dialog.ShowAsync();
        }
        public SearchResultPage()
        {
            this.InitializeComponent();
        }

        // 查询结果模型
        public class AppResult
        {
            public string bundleID { get; set; }
            public string id { get; set; }
            public string name { get; set; }
            public string price { get; set; }
            public string version { get; set; }
        }

        private List<AppResult> allResults = new List<AppResult>();
        private int currentPage = 1;
    private int pageSize = 15;
        private int totalPages = 1;

        public void SetResults(List<string> results)
        {
            allResults.Clear();
            foreach (var line in results)
            {
                var app = new AppResult
                {
                    bundleID = GetValue(line, "\"bundleID\":\"", "\""),
                    id = GetValue(line, "\"id\":", ","),
                    name = GetValue(line, "\"name\":\"", "\""),
                    price = GetValue(line, "\"price\":", ","),
                    version = GetValue(line, "\"version\":\"", "\"")
                };
                allResults.Add(app);
            }
            totalPages = (allResults.Count + pageSize - 1) / pageSize;
            currentPage = 1;
            UpdatePage();
        }

        private void UpdatePage()
        {
            int start = (currentPage - 1) * pageSize;
            int end = System.Math.Min(start + pageSize, allResults.Count);
            ResultList.ItemsSource = allResults.GetRange(start, end - start);
            // 只更新上下页按钮
            PrevPageButton.IsEnabled = currentPage > 1;
            NextPageButton.IsEnabled = currentPage < totalPages;
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentPage > 1)
            {
                currentPage--;
                UpdatePage();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentPage < totalPages)
            {
                currentPage++;
                UpdatePage();
            }
        }

        private string GetValue(string src, string start, string end)
        {
            int i1 = src.IndexOf(start);
            if (i1 < 0) return "";
            i1 += start.Length;
            int i2 = src.IndexOf(end, i1);
            if (i2 < 0) return src.Substring(i1);
            return src.Substring(i1, i2 - i1);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SearchPage));
        }
    }
}
