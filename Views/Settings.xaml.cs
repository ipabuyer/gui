using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using IPAbuyer.Common;
using IPAbuyer.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace IPAbuyer.Views
{
    public sealed partial class Settings : Page
    {
        private static readonly ResourceLoader Loader = new();
        private bool _isInitializingDetailedLogOption;
        private bool _isInitializingOwnedCheckOption;

        public Settings()
        {
            InitializeComponent();
            InitializeCountryCode();
            InitializeDownloadDirectory();
            InitializeDetailedIpatoolLogOption();
            InitializeOwnedCheckOption();
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
                Title = L("Settings.Dialog.ConfirmAction.Title", "确认操作"),
                Content = LF(
                    "Settings.Db.Clear.ConfirmMessage",
                    "确定要清空本地数据库中的已购买记录吗？{0}当前记录数：{1} 条。{0}此操作不可恢复。",
                    Environment.NewLine,
                    totalBefore),
                PrimaryButtonText = L("Settings.Dialog.ConfirmAction.Primary", "确认清空"),
                CloseButtonText = L("Settings.Dialog.ConfirmAction.Close", "取消"),
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
                await ShowDialogAsync(
                    L("Settings.Dialog.SuccessTitle", "操作成功"),
                    LF(
                        "Settings.Db.Clear.SuccessMessage",
                        "本地记录已清空。{0}清空前：{1} 条，清空后：{2} 条。",
                        Environment.NewLine,
                        totalBefore,
                        totalAfter));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings.Dialog.ErrorTitle", "错误"),
                    LF("Settings.Db.Clear.FailMessage", "清空失败：{0}", ex.Message));
            }
        }

        private void InitializeCountryCode()
        {
            try
            {
                string currentCode = KeychainConfig.GetCountryCode();
                if (CountryCodeValueTextBlockControl != null)
                {
                    CountryCodeValueTextBlockControl.Text = LF("Settings.CountryCode.CurrentFormat", "当前: {0}", currentCode);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化国家代码失败: {ex.Message}");
            }
        }

        private void InitializeDownloadDirectory()
        {
            try
            {
                if (DownloadDirectoryValueTextBlockControl != null)
                {
                    DownloadDirectoryValueTextBlockControl.Text = KeychainConfig.GetDownloadDirectory();
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

        private async Task HandleCountryCodeSubmissionAsync()
        {
            string currentCode = KeychainConfig.GetCountryCode();

            var inputBox = new TextBox
            {
                Text = currentCode,
                PlaceholderText = L("Settings.CountryCode.InputPlaceholder", "请输入两位国家/地区代码，例如 cn"),
                MaxLength = 2,
                Width = 220
            };

            var dialog = new ContentDialog
            {
                Title = L("Settings.CountryCode.DialogTitle", "设置国家/地区代码"),
                Content = inputBox,
                PrimaryButtonText = L("Settings.CountryCode.SaveButton", "保存"),
                CloseButtonText = L("Settings.CountryCode.CancelButton", "取消"),
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            string rawInput = inputBox.Text?.Trim() ?? string.Empty;
            bool inputWasEmpty = string.IsNullOrWhiteSpace(rawInput);
            string normalizedInput = inputWasEmpty ? "cn" : rawInput;

            if (!IsValidCountryCode(normalizedInput))
            {
                await ShowDialogAsync(
                    L("Settings.Dialog.OperationFailedTitle", "操作失败"),
                    L("Settings.CountryCode.InvalidMessage", "请输入合法的 ISO 3166-1 Alpha-2 国家/地区代码（2 位英文字母）。"));
                return;
            }

            string normalized = normalizedInput.ToLowerInvariant();

            try
            {
                KeychainConfig.SaveCountryCode(normalized);
                if (CountryCodeValueTextBlockControl != null)
                {
                    CountryCodeValueTextBlockControl.Text = LF("Settings.CountryCode.CurrentFormat", "当前: {0}", normalized);
                }

                MainPageCacheState.InvalidateSearchCache();

                string message = inputWasEmpty
                    ? L("Settings.CountryCode.EmptyResetMessage", "国家/地区代码为空，已恢复为默认值 cn")
                    : LF("Settings.CountryCode.UpdatedMessage", "国家/地区代码已更新为 {0}", normalized);

                await ShowDialogAsync(L("Settings.Dialog.SuccessTitle", "操作成功"), message);
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings.Dialog.OperationFailedTitle", "操作失败"),
                    LF("Settings.CountryCode.SaveFailMessage", "保存失败：{0}", ex.Message));
            }
        }

        private async void ResetCountryCodeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                KeychainConfig.SaveCountryCode("cn");
                if (CountryCodeValueTextBlockControl != null)
                {
                    CountryCodeValueTextBlockControl.Text = LF("Settings.CountryCode.CurrentFormat", "当前: {0}", "cn");
                }

                MainPageCacheState.InvalidateSearchCache();
                await ShowDialogAsync(
                    L("Settings.Dialog.SuccessTitle", "操作成功"),
                    L("Settings.CountryCode.ResetSuccessMessage", "国家/地区代码已恢复默认值 cn"));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings.Dialog.OperationFailedTitle", "操作失败"),
                    LF("Settings.CountryCode.ResetFailMessage", "恢复默认失败：{0}", ex.Message));
            }
        }

        private static bool IsValidCountryCode(string code)
        {
            return KeychainConfig.IsValidCountryCode(code);
        }

        private async void PickDownloadDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.Downloads
            };
            folderPicker.FileTypeFilter.Add("*");

            try
            {
                if (Application.Current is App app && app.MainWindowInstance != null)
                {
                    IntPtr hwnd = WindowNative.GetWindowHandle(app.MainWindowInstance);
                    InitializeWithWindow.Initialize(folderPicker, hwnd);
                }

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder == null)
                {
                    return;
                }

                KeychainConfig.SaveDownloadDirectory(folder.Path);
                if (DownloadDirectoryValueTextBlockControl != null)
                {
                    DownloadDirectoryValueTextBlockControl.Text = folder.Path;
                }

                await ShowDialogAsync(
                    L("Settings.Dialog.SuccessTitle", "操作成功"),
                    L("Settings.DownloadDir.UpdatedMessage", "下载目录已更新"));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings.Dialog.OperationFailedTitle", "操作失败"),
                    LF("Settings.DownloadDir.SaveFailMessage", "保存失败：{0}", ex.Message));
            }
        }

        private async void ResetDownloadDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string defaultDirectory = KeychainConfig.GetDefaultDownloadDirectory();
                KeychainConfig.SaveDownloadDirectory(defaultDirectory);
                if (DownloadDirectoryValueTextBlockControl != null)
                {
                    DownloadDirectoryValueTextBlockControl.Text = defaultDirectory;
                }

                await ShowDialogAsync(
                    L("Settings.Dialog.SuccessTitle", "操作成功"),
                    LF("Settings.DownloadDir.ResetSuccessMessage", "已恢复默认下载目录：{0}", defaultDirectory));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings.Dialog.OperationFailedTitle", "操作失败"),
                    LF("Settings.DownloadDir.ResetFailMessage", "恢复默认目录失败：{0}", ex.Message));
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
                await ShowDialogAsync(
                    L("Settings.Dialog.SuccessTitle", "操作成功"),
                    LF("Settings.Feedback.CopiedMessage", "反馈邮箱已复制：{0}", feedbackEmail));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings.Dialog.OperationFailedTitle", "操作失败"),
                    LF("Settings.Feedback.CopyFailMessage", "复制邮箱失败：{0}", ex.Message));
            }
        }

        private async void ClearIpatoolDataButton_Click(object sender, RoutedEventArgs e)
        {
            string ipatoolDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ipatool");

            var dialog = new ContentDialog
            {
                Title = L("Settings.Dialog.ConfirmAction.Title", "确认操作"),
                Content = LF(
                    "Settings.IpatoolData.ClearConfirmMessage",
                    "确定要清空以下目录吗？{0}{1}{0}此操作不可恢复。",
                    Environment.NewLine,
                    ipatoolDirectory),
                PrimaryButtonText = L("Settings.Dialog.ConfirmAction.Primary", "确认清空"),
                CloseButtonText = L("Settings.Dialog.ConfirmAction.Close", "取消"),
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            try
            {
                if (Directory.Exists(ipatoolDirectory))
                {
                    Directory.Delete(ipatoolDirectory, recursive: true);
                }

                Directory.CreateDirectory(ipatoolDirectory);
                await ShowDialogAsync(
                    L("Settings.Dialog.SuccessTitle", "操作成功"),
                    L("Settings.IpatoolData.ClearSuccessMessage", "ipatool 数据目录已清空。"));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings.Dialog.OperationFailedTitle", "操作失败"),
                    LF("Settings.IpatoolData.ClearFailMessage", "清空 ipatool 数据失败：{0}", ex.Message));
            }
        }

        private async void ExportIpatoolButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? sourcePath = ResolveBundledIpatoolPath();
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    await ShowDialogAsync(
                        L("Settings.Dialog.OperationFailedTitle", "操作失败"),
                        L("Settings.IpatoolExport.NotFoundMessage", "未在应用目录中找到可导出的 ipatool.exe。"));
                    return;
                }

                string outputDirectory = KeychainConfig.GetDownloadDirectory();
                Directory.CreateDirectory(outputDirectory);

                string targetPath = Path.Combine(outputDirectory, "ipatool.exe");
                if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                {
                    await ShowDialogAsync(
                        L("Settings.Dialog.SuccessTitle", "操作成功"),
                        LF("Settings.IpatoolExport.AlreadyInTargetMessage", "ipatool.exe 已位于目标目录：{0}", targetPath));
                    return;
                }

                File.Copy(sourcePath, targetPath, overwrite: true);
                await ShowDialogAsync(
                    L("Settings.Dialog.SuccessTitle", "操作成功"),
                    LF("Settings.IpatoolExport.SuccessMessage", "ipatool.exe 已导出到：{0}", targetPath));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings.Dialog.OperationFailedTitle", "操作失败"),
                    LF("Settings.IpatoolExport.FailMessage", "导出 ipatool.exe 失败：{0}", ex.Message));
            }
        }

        private void InitializeDetailedIpatoolLogOption()
        {
            if (DetailedIpatoolLogCheckBox == null)
            {
                return;
            }

            _isInitializingDetailedLogOption = true;
            try
            {
                DetailedIpatoolLogCheckBox.IsChecked = KeychainConfig.GetDetailedIpatoolLogEnabled();
            }
            finally
            {
                _isInitializingDetailedLogOption = false;
            }
        }

        private void DetailedIpatoolLogCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializingDetailedLogOption)
            {
                return;
            }

            KeychainConfig.SaveDetailedIpatoolLogEnabled(true);
        }

        private void DetailedIpatoolLogCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializingDetailedLogOption)
            {
                return;
            }

            KeychainConfig.SaveDetailedIpatoolLogEnabled(false);
        }

        private void InitializeOwnedCheckOption()
        {
            if (OwnedCheckBox == null)
            {
                return;
            }

            _isInitializingOwnedCheckOption = true;
            try
            {
                OwnedCheckBox.IsChecked = KeychainConfig.GetOwnedCheckEnabled();
            }
            finally
            {
                _isInitializingOwnedCheckOption = false;
            }
        }

        private void OwnedCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializingOwnedCheckOption)
            {
                return;
            }

            KeychainConfig.SaveOwnedCheckEnabled(true);
        }

        private void OwnedCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializingOwnedCheckOption)
            {
                return;
            }

            KeychainConfig.SaveOwnedCheckEnabled(false);
        }

        private static string? ResolveBundledIpatoolPath()
        {
            string baseDirectory = AppContext.BaseDirectory;
            string defaultPath = Path.Combine(baseDirectory, "ipatool.exe");
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }

            string includeDirectory = Path.Combine(baseDirectory, "Include");
            if (!Directory.Exists(includeDirectory))
            {
                return null;
            }

            string architectureSuffix = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "amd64",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(architectureSuffix))
            {
                return null;
            }

            string pattern = $"ipatool-*-windows-{architectureSuffix}.exe";
            return Directory.GetFiles(includeDirectory, pattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private async Task ShowDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = L("Settings.Dialog.CloseButton", "确定"),
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }

        private TextBlock? CountryCodeValueTextBlockControl => FindName("CountryCodeValueTextBlock") as TextBlock;
        private TextBlock? DownloadDirectoryValueTextBlockControl => FindName("DownloadDirectoryValueTextBlock") as TextBlock;

        private static string L(string key, string fallback)
        {
            string value = Loader.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            string format = L(key, fallback);
            return string.Format(format, args);
        }
    }
}
