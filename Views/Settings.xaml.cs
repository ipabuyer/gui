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
                Title = L("Settings/Dialog/ConfirmAction/Title"),
                Content = LF(
                    "Settings/Database/Clear/ConfirmMessage",
                    Environment.NewLine,
                    totalBefore),
                PrimaryButtonText = L("Settings/Dialog/ConfirmAction/Primary"),
                CloseButtonText = L("Settings/Dialog/ConfirmAction/Close"),
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
                    L("Settings/Dialog/SuccessTitle"),
                    LF(
                        "Settings/Database/Clear/SuccessMessage",
                        Environment.NewLine,
                        totalBefore,
                        totalAfter));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/ErrorTitle"),
                    LF("Settings/Database/Clear/FailMessage", ex.Message));
            }
        }

        private void InitializeCountryCode()
        {
            try
            {
                string currentCode = KeychainConfig.GetCountryCode();
                if (CountryCodeValueTextBlockControl != null)
                {
                    CountryCodeValueTextBlockControl.Text = LF("Settings/CountryCode/CurrentFormat", currentCode);
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
                PlaceholderText = L("Settings/CountryCode/InputPlaceholder"),
                MaxLength = 2,
                Width = 220
            };

            var dialog = new ContentDialog
            {
                Title = L("Settings/CountryCode/DialogTitle"),
                Content = inputBox,
                PrimaryButtonText = L("Settings/CountryCode/SaveButton"),
                CloseButtonText = L("Settings/CountryCode/CancelButton"),
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
                    L("Settings/Dialog/OperationFailedTitle"),
                    L("Settings/CountryCode/InvalidMessage"));
                return;
            }

            string normalized = normalizedInput.ToLowerInvariant();

            try
            {
                KeychainConfig.SaveCountryCode(normalized);
                if (CountryCodeValueTextBlockControl != null)
                {
                    CountryCodeValueTextBlockControl.Text = LF("Settings/CountryCode/CurrentFormat", normalized);
                }

                MainPageCacheState.InvalidateSearchCache();

                string message = inputWasEmpty
                    ? L("Settings/CountryCode/EmptyResetMessage")
                    : LF("Settings/CountryCode/UpdatedMessage", normalized);

                await ShowDialogAsync(L("Settings/Dialog/SuccessTitle"), message);
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/OperationFailedTitle"),
                    LF("Settings/CountryCode/SaveFailMessage", ex.Message));
            }
        }

        private async void ResetCountryCodeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                KeychainConfig.SaveCountryCode("cn");
                if (CountryCodeValueTextBlockControl != null)
                {
                    CountryCodeValueTextBlockControl.Text = LF("Settings/CountryCode/CurrentFormat", "cn");
                }

                MainPageCacheState.InvalidateSearchCache();
                await ShowDialogAsync(
                    L("Settings/Dialog/SuccessTitle"),
                    L("Settings/CountryCode/ResetSuccessMessage"));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/OperationFailedTitle"),
                    LF("Settings/CountryCode/ResetFailMessage", ex.Message));
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
                    L("Settings/Dialog/SuccessTitle"),
                    L("Settings/DownloadDirectory/UpdatedMessage"));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/OperationFailedTitle"),
                    LF("Settings/DownloadDirectory/SaveFailMessage", ex.Message));
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
                    L("Settings/Dialog/SuccessTitle"),
                    LF("Settings/DownloadDirectory/ResetSuccessMessage", defaultDirectory));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/OperationFailedTitle"),
                    LF("Settings/DownloadDirectory/ResetFailMessage", ex.Message));
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
                    L("Settings/Dialog/SuccessTitle"),
                    LF("Settings/Feedback/CopiedMessage", feedbackEmail));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/OperationFailedTitle"),
                    LF("Settings/Feedback/CopyFailMessage", ex.Message));
            }
        }

        private async void ClearIpatoolDataButton_Click(object sender, RoutedEventArgs e)
        {
            string ipatoolDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ipatool");

            var dialog = new ContentDialog
            {
                Title = L("Settings/Dialog/ConfirmAction/Title"),
                Content = LF(
                    "Settings/IpatoolData/ClearConfirmMessage",
                    Environment.NewLine,
                    ipatoolDirectory),
                PrimaryButtonText = L("Settings/Dialog/ConfirmAction/Primary"),
                CloseButtonText = L("Settings/Dialog/ConfirmAction/Close"),
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
                    L("Settings/Dialog/SuccessTitle"),
                    L("Settings/IpatoolData/ClearSuccessMessage"));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/OperationFailedTitle"),
                    LF("Settings/IpatoolData/ClearFailMessage", ex.Message));
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
                        L("Settings/Dialog/OperationFailedTitle"),
                        L("Settings/IpatoolExport/NotFoundMessage"));
                    return;
                }

                string outputDirectory = KeychainConfig.GetDownloadDirectory();
                Directory.CreateDirectory(outputDirectory);

                string targetPath = Path.Combine(outputDirectory, "ipatool.exe");
                if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                {
                    await ShowDialogAsync(
                        L("Settings/Dialog/SuccessTitle"),
                        LF("Settings/IpatoolExport/AlreadyInTargetMessage", targetPath));
                    return;
                }

                File.Copy(sourcePath, targetPath, overwrite: true);
                await ShowDialogAsync(
                    L("Settings/Dialog/SuccessTitle"),
                    LF("Settings/IpatoolExport/SuccessMessage", targetPath));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/OperationFailedTitle"),
                    LF("Settings/IpatoolExport/FailMessage", ex.Message));
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
                CloseButtonText = L("Settings/Dialog/CloseButton"),
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }

        private TextBlock? CountryCodeValueTextBlockControl => FindName("CountryCodeValueTextBlock") as TextBlock;
        private TextBlock? DownloadDirectoryValueTextBlockControl => FindName("DownloadDirectoryValueTextBlock") as TextBlock;

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private static string LF(string key, params object[] args)
        {
            string format = L(key);
            return string.Format(format, args);
        }
    }
}
