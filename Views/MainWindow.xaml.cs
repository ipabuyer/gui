using IPAbuyer.Common;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace IPAbuyer.Views
{
    public sealed partial class MainWindow : Window
    {
        private static readonly ResourceLoader Loader = new();
        private MainPage? _currentMainPage;
        private AppWindow? _appWindow;

        public MainWindow()
        {
            InitializeComponent();
            _appWindow = GetAppWindow(this);
            ConfigureSystemBackdrop();

            ExtendsContentIntoTitleBar = true;
            ApplyCaptionButtonColors();
            AppTitleBar.ActualThemeChanged += (_, _) => ApplyCaptionButtonColors();

            Title = L("MainWindow/TitleBarTitle");
            AppTitleBar.Title = Title;
            AppTitleBar.Subtitle = L("MainWindow/TitleBarSubtitle");
            SetWindowIcon(this);
            UpdateLoginStatusPicture();
            SessionState.LoginStateChanged -= OnLoginStateChanged;
            SessionState.LoginStateChanged += OnLoginStateChanged;
            Closed += MainWindow_Closed;

            ContentFrame.Navigated += ContentFrame_Navigated;
            ContentFrame.Navigate(typeof(MainPage));
            foreach (var menuItem in NavView.MenuItems)
            {
                if (menuItem is NavigationViewItem nvi && nvi.Tag?.ToString() == "MainPage")
                {
                    NavView.SelectedItem = nvi;
                    break;
                }
            }

            UpdateSearchBoxState();
        }

        private void ConfigureSystemBackdrop()
        {
            try
            {
                SystemBackdrop = new MicaBackdrop();
            }
            catch
            {
                // ignore on unsupported systems
            }
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private static string LF(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key), args);
        }

        private void AppNameBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            TriggerSearch();
        }

        private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        private void TriggerSearch()
        {
            if (_currentMainPage == null)
            {
                return;
            }

            string appName = AppNameBox.Text?.Trim() ?? string.Empty;
            if (ContentFrame.Content is MainPage mainPage)
            {
                mainPage.PerformSearchFromMainWindow(appName);
            }
        }

        private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            _currentMainPage = ContentFrame.Content as MainPage;

            UpdateSearchBoxState();
        }

        private static void SetWindowIcon(Window window)
        {
            try
            {
                var appWindow = GetAppWindow(window);

                string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    appWindow?.SetIcon(iconPath);
                }
            }
            catch
            {
                // ignore icon load failures
            }
        }

        private static AppWindow? GetAppWindow(Window window)
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }

        private void ApplyCaptionButtonColors()
        {
            if (_appWindow?.TitleBar == null)
            {
                return;
            }

            bool isDark = AppTitleBar.ActualTheme == ElementTheme.Dark;
            var foreground = isDark ? Colors.White : Colors.Black;
            var hoverBackground = isDark
                ? Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x1A, 0x00, 0x00, 0x00);
            var pressedBackground = isDark
                ? Windows.UI.Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x26, 0x00, 0x00, 0x00);

            _appWindow.TitleBar.ButtonForegroundColor = foreground;
            _appWindow.TitleBar.ButtonInactiveForegroundColor = foreground;
            _appWindow.TitleBar.ButtonHoverForegroundColor = foreground;
            _appWindow.TitleBar.ButtonPressedForegroundColor = foreground;
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonHoverBackgroundColor = hoverBackground;
            _appWindow.TitleBar.ButtonPressedBackgroundColor = pressedBackground;
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is NavigationViewItem item)
            {
                switch (item.Tag)
                {
                    case "MainPage":
                        ContentFrame.Navigate(typeof(MainPage));
                        break;
                    case "LoginPage":
                        ContentFrame.Navigate(typeof(LoginPage));
                        break;
                    case "IpatoolPage":
                        ContentFrame.Navigate(typeof(IpatoolPage));
                        break;
                    case "SettingsPage":
                        ContentFrame.Navigate(typeof(Settings));
                        break;
                }
            }
        }

        private void UpdateSearchBoxState()
        {
            bool isMainPage = _currentMainPage != null;
            AppNameBox.IsEnabled = isMainPage;
        }

        private void OnLoginStateChanged()
        {
            DispatcherQueue.TryEnqueue(UpdateLoginStatusPicture);
        }

        private void UpdateLoginStatusPicture()
        {
            bool isLoggedIn = SessionState.IsLoggedIn;
            LoginStatusPicture.Background = new SolidColorBrush(isLoggedIn
                ? Windows.UI.Color.FromArgb(0xFF, 0x2E, 0xA0, 0x43)
                : Windows.UI.Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C));
            LoginStatusPicture.DisplayName = string.Empty;
            ToolTipService.SetToolTip(LoginStatusPicture, isLoggedIn
                ? LF("MainWindow/LoginStatus/LoggedIn", SessionState.CurrentAccount)
                : L("MainWindow/LoginStatus/LoggedOut"));
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            SessionState.LoginStateChanged -= OnLoginStateChanged;
        }

    }
}


