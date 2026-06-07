using IPAbuyer.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace IPAbuyer.Views
{
    public sealed partial class IpatoolPage : Page
    {
        private static readonly ResourceLoader Loader = new();
        private const string IpatoolGitRevision = "dcddce4650d49d64aaff1b0785d76de01f5227af";
        private bool _isInitializing;
        private bool _isInitializingDetailedLogOption;

        public IpatoolPage()
        {
            InitializeComponent();
            InitializeMainGitRevision();
            InitializeSelection();
            InitializeDetailedIpatoolLogOption();
        }

        private void InitializeMainGitRevision()
        {
            string shortRevision = IpatoolGitRevision[..7];
            MainGitRevisionTextBlock.Text = LF("IpatoolPage/Main/GitRevisionShortFormat", shortRevision);
            ToolTipService.SetToolTip(
                MainGitRevisionTextBlock,
                LF("IpatoolPage/Main/GitRevisionTooltipFormat", IpatoolGitRevision));
        }

        private void InitializeSelection()
        {
            UpdateSelectionButtons(KeychainConfig.GetIpatoolFlavor());
        }

        private async void IpatoolFlavorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || sender is not Button button || button.Tag is not string flavor)
            {
                return;
            }

            if (string.Equals(flavor, KeychainConfig.IpatoolFlavorCustom, StringComparison.OrdinalIgnoreCase)
                && !KeychainConfig.HasUsableCustomIpatoolPath())
            {
                await PickCustomIpatoolAsync(selectAfterPick: true);
                return;
            }

            KeychainConfig.SaveIpatoolFlavor(flavor);
            UpdateSelectionButtons(flavor);
        }

        private void OpenRepositoryButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/majd/ipatool",
                UseShellExecute = true
            });
        }

        private async void ExportIpatoolMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem menuItem || menuItem.Tag is not string flavor)
            {
                return;
            }

            try
            {
                string outputDirectory = KeychainConfig.GetDownloadDirectory();
                string displayName = GetFlavorDisplayName(flavor);
                var confirmDialog = new ContentDialog
                {
                    Title = L("IpatoolPage/Export/ConfirmTitle"),
                    Content = LF("IpatoolPage/Export/ConfirmMessage", displayName, outputDirectory),
                    PrimaryButtonText = L("IpatoolPage/Export/ConfirmPrimary"),
                    CloseButtonText = L("Settings/Dialog/ConfirmAction/Close"),
                    XamlRoot = XamlRoot
                };

                if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                string? sourcePath = ResolveBundledIpatoolPath(flavor);
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    await ShowDialogAsync(
                        L("Settings/Dialog/OperationFailedTitle"),
                        LF("IpatoolPage/Export/NotFoundMessage", displayName));
                    return;
                }

                Directory.CreateDirectory(outputDirectory);

                string targetPath = Path.Combine(outputDirectory, "ipatool.exe");
                if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                {
                    await ShowDialogAsync(
                        L("Settings/Dialog/SuccessTitle"),
                        LF("IpatoolPage/Export/AlreadyInTargetMessage", targetPath));
                    return;
                }

                if (File.Exists(targetPath))
                {
                    var overwriteDialog = new ContentDialog
                    {
                        Title = L("IpatoolPage/Export/OverwriteTitle"),
                        Content = LF("IpatoolPage/Export/OverwriteMessage", targetPath),
                        PrimaryButtonText = L("IpatoolPage/Export/OverwritePrimary"),
                        CloseButtonText = L("Settings/Dialog/ConfirmAction/Close"),
                        XamlRoot = XamlRoot
                    };

                    if (await overwriteDialog.ShowAsync() != ContentDialogResult.Primary)
                    {
                        return;
                    }
                }

                File.Copy(sourcePath, targetPath, overwrite: true);
                await ShowDialogAsync(
                    L("Settings/Dialog/SuccessTitle"),
                    LF("IpatoolPage/Export/SuccessMessage", displayName, targetPath));
                RevealExportedFile(targetPath);
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/OperationFailedTitle"),
                    LF("IpatoolPage/Export/FailMessage", ex.Message));
            }
        }

        private async void PickCustomIpatoolMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await PickCustomIpatoolAsync(selectAfterPick: true);
        }

        private async void DeleteCustomIpatoolMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(KeychainConfig.GetCustomIpatoolPath()))
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = L("IpatoolPage/Custom/DeleteConfirmTitle"),
                Content = L("IpatoolPage/Custom/DeleteConfirmMessage"),
                PrimaryButtonText = L("IpatoolPage/Custom/DeleteConfirmPrimary"),
                CloseButtonText = L("Settings/Dialog/ConfirmAction/Close"),
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            KeychainConfig.DeleteCustomIpatoolPath();
            UpdateSelectionButtons(KeychainConfig.GetIpatoolFlavor());
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
                    "IpatoolPage/Data/ClearConfirmMessage",
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
                    L("IpatoolPage/Data/ClearSuccessMessage"));
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/OperationFailedTitle"),
                    LF("IpatoolPage/Data/ClearFailMessage", ex.Message));
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
                DetailedIpatoolLogCheckBox.IsOn = KeychainConfig.GetDetailedIpatoolLogEnabled();
            }
            finally
            {
                _isInitializingDetailedLogOption = false;
            }
        }

        private void DetailedIpatoolLogCheckBox_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializingDetailedLogOption)
            {
                return;
            }

            KeychainConfig.SaveDetailedIpatoolLogEnabled(DetailedIpatoolLogCheckBox.IsOn);
        }

        private void UpdateSelectionButtons(string flavor)
        {
            _isInitializing = true;
            try
            {
                bool isMainSelected = string.Equals(flavor, KeychainConfig.IpatoolFlavorMain, StringComparison.OrdinalIgnoreCase);
                bool isCustomSelected = string.Equals(flavor, KeychainConfig.IpatoolFlavorCustom, StringComparison.OrdinalIgnoreCase);
                string customPath = KeychainConfig.GetCustomIpatoolPath();
                bool hasCustomPath = !string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath);
                MainSelectButton.Content = isMainSelected
                    ? L("IpatoolPage/Button/Current")
                    : L("IpatoolPage/Button/Switch");
                MainSelectButton.IsEnabled = !isMainSelected;
                ReleaseSelectButton.Content = string.Equals(flavor, KeychainConfig.IpatoolFlavorLegacy, StringComparison.OrdinalIgnoreCase)
                    ? L("IpatoolPage/Button/Current")
                    : L("IpatoolPage/Button/Switch");
                ReleaseSelectButton.IsEnabled = !string.Equals(flavor, KeychainConfig.IpatoolFlavorLegacy, StringComparison.OrdinalIgnoreCase);
                CustomIpatoolPathTextBlock.Text = hasCustomPath
                    ? customPath
                    : L("IpatoolPage/Custom/EmptyPath");
                ToolTipService.SetToolTip(CustomIpatoolPathTextBlock, hasCustomPath ? customPath : null);
                CustomSelectButton.Content = hasCustomPath
                    ? (isCustomSelected ? L("IpatoolPage/Button/Current") : L("IpatoolPage/Button/Switch"))
                    : L("IpatoolPage/Button/Pick");
                CustomSelectButton.IsEnabled = !hasCustomPath || !isCustomSelected;
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private async Task PickCustomIpatoolAsync(bool selectAfterPick)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.Downloads
            };
            picker.FileTypeFilter.Add(".exe");

            if (Application.Current is App app && app.MainWindowInstance != null)
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(app.MainWindowInstance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
            if (file == null)
            {
                return;
            }

            if (!string.Equals(file.Name, "ipatool.exe", StringComparison.OrdinalIgnoreCase))
            {
                var dialog = new ContentDialog
                {
                    Title = L("IpatoolPage/Custom/InvalidFileTitle"),
                    Content = L("IpatoolPage/Custom/InvalidFileMessage"),
                    CloseButtonText = L("Settings/Dialog/CloseButton"),
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            try
            {
                KeychainConfig.SaveCustomIpatoolPath(file.Path);
                if (selectAfterPick)
                {
                    KeychainConfig.SaveIpatoolFlavor(KeychainConfig.IpatoolFlavorCustom);
                }

                UpdateSelectionButtons(KeychainConfig.GetIpatoolFlavor());
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/OperationFailedTitle"),
                    LF("IpatoolPage/Custom/SaveFailMessage", ex.Message));
            }
        }

        private static string? ResolveBundledIpatoolPath(string flavor)
        {
            bool isRelease = string.Equals(flavor, KeychainConfig.IpatoolFlavorLegacy, StringComparison.OrdinalIgnoreCase);
            string baseDirectory = AppContext.BaseDirectory;
            string defaultPath = Path.Combine(baseDirectory, isRelease ? "ipatool-legacy.exe" : "ipatool.exe");
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

            string pattern = isRelease
                ? $"ipatool-2.3.0-windows-{architectureSuffix}.exe"
                : $"ipatool-main-windows-{architectureSuffix}.exe";
            return Directory.GetFiles(includeDirectory, pattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static void RevealExportedFile(string path)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch
            {
                // 导出已经完成，忽略打开资源管理器失败。
            }
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

        private static string GetFlavorDisplayName(string flavor)
        {
            return string.Equals(flavor, KeychainConfig.IpatoolFlavorLegacy, StringComparison.OrdinalIgnoreCase)
                ? L("IpatoolPage/Release/DisplayName")
                : L("IpatoolPage/Main/DisplayName");
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private static string LF(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key), args);
        }
    }
}
