using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using IPAbuyer;
using System;

namespace IPAbuyer.Views
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);

            Title = "IPAbuyer - 快速购买 AppStore 中的应用";
            SetWindowIcon(this);

            ContentFrame.Navigate(typeof(MainPage));
            foreach (var menuItem in NavView.MenuItems)
            {
                if (menuItem is NavigationViewItem nvi && nvi.Tag?.ToString() == "MainPage")
                {
                    NavView.SelectedItem = nvi;
                    break;
                }
            }
        }

        private void PaneToggleButton_Click(object sender, RoutedEventArgs e)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        private void AppNameBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            TriggerSearch();
        }

        private void TriggerSearch()
        {
            string appName = AppNameBox.Text?.Trim() ?? string.Empty;
            if (ContentFrame.Content is MainPage mainPage)
            {
                mainPage.PerformSearchFromMainWindow(appName);
            }
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
                    case "DownloadsPage":
                        ContentFrame.Navigate(typeof(DownloadsPage));
                        break;
                    case "SettingsPage":
                        ContentFrame.Navigate(typeof(Settings));
                        break;
                }
            }
        }
    }
}