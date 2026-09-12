using IPAbuyer.Core.Configuration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace IPAbuyer.Pages
{
    public sealed partial class IpatoolPage : Page
    {
        private static readonly ResourceLoader Loader = new();
        private bool _isInitializingDetailedLogOption;

        public IpatoolPage()
        {
            InitializeComponent();
            UpdateCustomIpatoolPath();
            InitializeDetailedIpatoolLogOption();
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
            try
            {
                string outputDirectory = ApplicationSettings.GetDownloadDirectory();
                string displayName = L("IpatoolPage/Release/DisplayName");
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

                string? sourcePath = ResolveBundledIpatoolPath();
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

        private void UseBundledIpatoolButton_Click(object sender, RoutedEventArgs e)
        {
            IpatoolSettings.SaveFlavor(IpatoolSettings.FlavorMain);
            UpdateCustomIpatoolPath();
        }

        private void UseCustomIpatoolButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IpatoolSettings.HasUsableCustomPath())
            {
                return;
            }

            IpatoolSettings.SaveFlavor(IpatoolSettings.FlavorCustom);
            UpdateCustomIpatoolPath();
        }

        private async void PickCustomIpatoolButton_Click(object sender, RoutedEventArgs e)
        {
            await PickCustomIpatoolAsync();
        }

        private async void DeleteCustomIpatoolMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(IpatoolSettings.GetCustomPath()))
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

            IpatoolSettings.DeleteCustomPath();
            UpdateCustomIpatoolPath();
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
            _isInitializingDetailedLogOption = true;
            try
            {
                DetailedIpatoolLogCheckBox.IsOn = ApplicationSettings.GetDetailedIpatoolLogEnabled();
            }
            finally
            {
                _isInitializingDetailedLogOption = false;
            }
        }

        private void DetailedIpatoolLogCheckBox_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isInitializingDetailedLogOption)
            {
                ApplicationSettings.SaveDetailedIpatoolLogEnabled(DetailedIpatoolLogCheckBox.IsOn);
            }
        }

        private void UpdateCustomIpatoolPath()
        {
            string customPath = IpatoolSettings.GetCustomPath();
            bool hasCustomPath = !string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath);
            bool isCustomSelected = hasCustomPath
                && string.Equals(IpatoolSettings.GetFlavor(), IpatoolSettings.FlavorCustom, StringComparison.OrdinalIgnoreCase);
            CustomIpatoolPathTextBlock.Text = hasCustomPath
                ? customPath
                : L("IpatoolPage/Custom/EmptyPath");
            ToolTipService.SetToolTip(CustomIpatoolPathTextBlock, hasCustomPath ? customPath : null);
            CustomSelectButton.Content = hasCustomPath
                ? L("IpatoolPage/Button/Replace")
                : L("IpatoolPage/Button/Pick");

            string currentText = L("IpatoolPage/Badge/Current");
            ReleaseCurrentBadgeTextBlock.Text = currentText;
            ReleaseCurrentBadge.Visibility = isCustomSelected ? Visibility.Collapsed : Visibility.Visible;
            ReleaseSelectButton.Visibility = isCustomSelected ? Visibility.Visible : Visibility.Collapsed;
            CustomCurrentBadgeTextBlock.Text = currentText;
            CustomCurrentBadge.Visibility = isCustomSelected ? Visibility.Visible : Visibility.Collapsed;
            CustomUseButton.Visibility = hasCustomPath && !isCustomSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task PickCustomIpatoolAsync()
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

            try
            {
                IpatoolSettings.SaveCustomPath(file.Path);
                UpdateCustomIpatoolPath();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    L("Settings/Dialog/OperationFailedTitle"),
                    LF("IpatoolPage/Custom/SaveFailMessage", ex.Message));
            }
        }

        private static string? ResolveBundledIpatoolPath()
        {
            string baseDirectory = AppContext.BaseDirectory;
            string defaultPath = Path.Combine(baseDirectory, "ipatool.exe");
            if (File.Exists(defaultPath))
            {
                return defaultPath;
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

            string includePath = Path.Combine(baseDirectory, "Include", $"ipatool-2.5.0-windows-{architectureSuffix}.exe");
            return File.Exists(includePath) ? includePath : null;
        }

        private static void RevealExportedFile(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
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
