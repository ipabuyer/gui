using IPAbuyer.Data;
using IPAbuyer.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

namespace IPAbuyer.Views
{
    public sealed partial class MainPage : Page
    {
        // ������Χ����
        public int SearchLimitNum { get; set; } = 5;

        private void SearchLimit_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            SearchLimitNum = (int)e.NewValue;
            var tb = this.FindName("SearchLimitNumText") as TextBlock;
            if (tb != null) tb.Text = SearchLimitNum.ToString();
        }
        // ��ѯ�������ģ��
        public class AppResult
        {
            public string? bundleID { get; set; }
            public string? id { get; set; }
            public string? name { get; set; }
            public string? price { get; set; }
            public string? version { get; set; }
            public string? purchased { get; set; }
        }

        private List<AppResult> allResults = new List<AppResult>();
        private int currentPage = 1;
        private int pageSize = 15;
        private int totalPages = 1;

        // ��ҳ��ť�¼�
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

        // ���������¼�
        private async void BatchPurchaseButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ResultList.SelectedItems;
            if (selected == null || selected.Count == 0)
            {
                ResultText.Text = "����ѡ��Ҫ��������Ŀɹ����";
                return;
            }
            int success = 0, fail = 0, skip = 0;
            foreach (var item in selected)
            {
                if (item is AppResult app && app.purchased == "�ɹ���" && app.price == "0")
                {
                    string cmd = $"./ipatool.exe purchase --keychain-passphrase 12345678 --bundle-identifier {app.bundleID}";
                    string result = await RunCommandAsync(cmd);
                    if ((result.Contains("success") && result.Contains("true")) || result.Contains("����ɹ�"))
                    {
                        success++;
                    }
                    else
                    {
                        fail++;
                    }
                }
                else
                {
                    skip++;
                }
            }
            ResultText.Text = $"����������ɣ��ɹ�{success}��ʧ��{fail}������{skip}";
            RefreshPurchasedStatus();
        }

        // ˢ���ѹ���״̬
        private void RefreshPurchasedStatus()
        {
            // ���¼���ѹ���״̬�����б������ݿ�ɼ��ɣ�
            foreach (var app in allResults)
            {
                // ʾ��������purchased�ֶ�����ȷ����
                // �ɸ���ʵ��ҵ������ݿ��ѯ�ѹ�bundleID
            }
            UpdatePage();
        }

        // ��ҳ����ˢ��
        private void UpdatePage()
        {
            int start = (currentPage - 1) * pageSize;
            int end = System.Math.Min(start + pageSize, allResults.Count);
            ResultList.ItemsSource = allResults.GetRange(start, end - start);
        }
        private const string keychainPassphrase = "12345678";
        public MainPage()
        {
            this.InitializeComponent();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string appName = AppNameBox.Text.Trim();
            if (string.IsNullOrEmpty(appName))
            {
                ResultText.Text = "������APP����";
                return;
            }
            SearchButton.IsEnabled = false;
            string cmd = $"./ipatool.exe search --keychain-passphrase {keychainPassphrase} {appName} --limit {SearchLimitNum} --non-interactive --format json";
            var result = await RunCommandAsync(cmd);
            SearchButton.IsEnabled = true;

            // ֱ�ӽ���JSON���󣬱���apps����
            this.allResults.Clear();
            int successCount = 0, failCount = 0;
            // ��ȡ�ѹ���ʷ bundleID �б�
            var purchasedList = IPAbuyer.Data.PurchasedAppDb.GetPurchasedApps().Select(x => x.bundleID).ToHashSet();
            try
            {
                var root = System.Text.Json.JsonDocument.Parse(result).RootElement;
                if (root.TryGetProperty("apps", out var apps) && apps.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var obj in apps.EnumerateArray())
                    {
                        try
                        {
                            var bundleId = obj.TryGetProperty("bundleID", out var bid) ? bid.GetString() : obj.TryGetProperty("bundleId", out var bid2) ? bid2.GetString() : "";
                            var app = new AppResult
                            {
                                bundleID = bundleId,
                                id = obj.TryGetProperty("id", out var idv) ? idv.GetRawText() : "",
                                name = obj.TryGetProperty("name", out var namev) ? namev.GetString() : "",
                                price = obj.TryGetProperty("price", out var pricev) ? pricev.GetRawText() : "",
                                version = obj.TryGetProperty("version", out var ver) ? ver.GetString() : "",
                                purchased = purchasedList.Contains(bundleId) ? "�ѹ�" : "�ɹ���"
                            };
                            this.allResults.Add(app);
                            successCount++;
                        }
                        catch { failCount++; }
                    }
                }
            }
            catch { failCount++; }
            // ������ҳ��
            totalPages = (allResults.Count + pageSize - 1) / pageSize;
            currentPage = 1;
            UpdatePage();
            ResultText.Text = $"�����ɹ� {successCount} ����ʧ�� {failCount} �������� {allResults.Count}";
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
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
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
                return $"����ִ��ʧ��: {ex.Message}";
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(Settings));
        }
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            IPAbuyer.Data.AccountHistoryDb.SetLogoutFlag();
            Frame.Navigate(typeof(LoginPage));
        }
        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            UpdatePage();
            //Frame.Navigate(typeof(SettingPage));
        }
    }
}
