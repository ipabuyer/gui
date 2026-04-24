using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using IPAbuyer;
using System;

namespace IPAbuyer.Views
{
    public sealed partial class MainWindow : Window
    {
        private MainPage? _currentMainPage;

        public MainWindow()
        {
            InitializeComponent();
            ConfigureSystemBackdrop();

            ExtendsContentIntoTitleBar = true;

            Title = TitleBarTextBlock.Text;
            SetWindowIcon(this);

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
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

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
                    case "SettingsPage":
                        ContentFrame.Navigate(typeof(Settings));
                        break;
                }
            }
        }

        private void UpdateSearchBoxState()
        {
            bool isMainPage = _currentMainPage != null;
            AppTitleBar.Content = isMainPage ? SearchBoxHost : null;
        }

    }
}


