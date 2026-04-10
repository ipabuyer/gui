using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using IPAbuyer;
using System;
using System.Collections.Generic;
using Windows.Graphics;

namespace IPAbuyer.Views
{
    public sealed partial class MainWindow : Window
    {
        private MainPage? _currentMainPage;
        private readonly InputNonClientPointerSource? _nonClientPointerSource;

        public MainWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);
            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                _nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(windowId);
            }
            catch
            {
                _nonClientPointerSource = null;
            }

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
            CustomTitleBar.Loaded += TitleBar_Loaded;
            SizeChanged += MainWindow_SizeChanged;
            CustomTitleBar.SizeChanged += TitleBarElement_SizeChanged;
            PaneToggleButton.SizeChanged += TitleBarElement_SizeChanged;
            AppNameBox.SizeChanged += TitleBarElement_SizeChanged;
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
            if (_currentMainPage != null)
            {
                _currentMainPage.SearchLoadingChanged -= MainPage_SearchLoadingChanged;
            }

            _currentMainPage = ContentFrame.Content as MainPage;
            if (_currentMainPage != null)
            {
                _currentMainPage.SearchLoadingChanged += MainPage_SearchLoadingChanged;
            }
            else
            {
                SearchLoadingBar.Visibility = Visibility.Collapsed;
            }

            UpdateSearchBoxState();
        }

        private void MainPage_SearchLoadingChanged(bool isLoading)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                SearchLoadingBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            });
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

        private void UpdateSearchBoxState()
        {
            bool isMainPage = _currentMainPage != null;
            AppNameBox.IsEnabled = isMainPage;
            AppNameBox.Opacity = isMainPage ? 1.0 : 0.65;
            UpdateNonClientPassthroughRegions();
        }

        private void TitleBar_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateNonClientPassthroughRegions();
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            UpdateNonClientPassthroughRegions();
        }

        private void TitleBarElement_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateNonClientPassthroughRegions();
        }

        private void UpdateNonClientPassthroughRegions()
        {
            if (_nonClientPointerSource == null)
            {
                return;
            }

            double scale = CustomTitleBar.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0 || CustomTitleBar.ActualWidth <= 0 || CustomTitleBar.ActualHeight <= 0)
            {
                return;
            }

            try
            {
                var passthroughRects = new List<RectInt32>();
                TryAddPassthroughRect(PaneToggleButton, scale, passthroughRects);
                TryAddPassthroughRect(AppNameBox, scale, passthroughRects);
                _nonClientPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, passthroughRects.ToArray());
            }
            catch
            {
                // 蹇界暐鍖哄煙鏇存柊澶辫触锛岄伩鍏嶅湪鐗瑰畾绯荤粺鐜涓嬭Е鍙戝惎鍔ㄥ穿婧冦€?
            }
        }

        private void TryAddPassthroughRect(FrameworkElement element, double scale, List<RectInt32> rects)
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return;
            }

            var transform = element.TransformToVisual(CustomTitleBar);
            Windows.Foundation.Point origin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            int x = Math.Max(0, (int)Math.Round(origin.X * scale));
            int y = Math.Max(0, (int)Math.Round(origin.Y * scale));
            int width = Math.Max(1, (int)Math.Round(element.ActualWidth * scale));
            int height = Math.Max(1, (int)Math.Round(element.ActualHeight * scale));

            rects.Add(new RectInt32(x, y, width, height));
        }

    }
}


