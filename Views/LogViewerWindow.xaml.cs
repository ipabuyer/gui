using IPAbuyer.Common;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WinRT.Interop;

namespace IPAbuyer.Views
{
    public sealed partial class LogViewerWindow : Window
    {
        private static readonly ResourceLoader Loader = new();
        private static LogViewerWindow? ActiveWindow;

        private readonly Func<IReadOnlyList<UiLogEntry>> _entryProvider;
        private readonly DispatcherQueueTimer _refreshTimer;
        private readonly Window? _ownerWindow;
        private int _lastLogSignature = int.MinValue;
        private bool _hasStartedRefreshTimer;
        private bool _isClosed;
        private const int DefaultWindowWidth = 1100;
        private const int DefaultWindowHeight = 700;

        public static void ShowOrActivate(Window? ownerWindow)
        {
            if (ActiveWindow != null)
            {
                ActiveWindow.Activate();
                return;
            }

            ActiveWindow = new LogViewerWindow(
                UiLogStore.GetSnapshot,
                ownerWindow);
            ActiveWindow.Closed += (_, _) =>
            {
                ActiveWindow = null;
            };
            ActiveWindow.Activate();
        }

        public LogViewerWindow(
            Func<IReadOnlyList<UiLogEntry>> entryProvider,
            Window? ownerWindow)
        {
            InitializeComponent();

            _entryProvider = entryProvider;
            _ownerWindow = ownerWindow;

            Title = L("Common/LogDialog/Title");
            CopyButton.Content = L("Common/LogDialog/CopyButton");
            ClearButton.Content = L("Common/LogDialog/ClearButton");
            CloseButton.Content = L("Common/LogDialog/CloseButton");

            _refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(250);
            _refreshTimer.Tick += (_, _) => RefreshLogIfChanged();

            ConfigureSystemBackdrop();
            ConfigureWindow(ownerWindow);
            Activated += LogViewerWindow_Activated;
            Closed += LogViewerWindow_Closed;

            RefreshLog();
        }

        private void LogViewerWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_hasStartedRefreshTimer)
            {
                return;
            }

            _hasStartedRefreshTimer = true;
            _lastLogSignature = int.MinValue;
            RefreshLogIfChanged();
            QueueScrollToBottom();
            _refreshTimer.Start();
        }

        private void LogViewerWindow_Closed(object sender, WindowEventArgs args)
        {
            _isClosed = true;
            _refreshTimer.Stop();
            if (_ownerWindow != null)
            {
                _ownerWindow.Closed -= OwnerWindow_Closed;
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            string text = UiLogStore.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                UiLogStore.Append(L("Common/LogDialog/CopyEmptyLog"), UiLogLevel.Tip);
                RefreshLog();
                QueueScrollToBottom();
                return;
            }

            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            UiLogStore.Append(L("Common/LogDialog/CopiedToClipboard"), UiLogLevel.Success);
            RefreshLog();
            QueueScrollToBottom();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            UiLogStore.Clear();
            UiLogStore.Append(L("Common/LogDialog/Cleared"), UiLogLevel.Info);
            RefreshLog();
            QueueScrollToBottom();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ConfigureWindow(Window? ownerWindow)
        {
            IntPtr windowHandle = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
            AppWindow? appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow?.Resize(new SizeInt32(DefaultWindowWidth, DefaultWindowHeight));
            SetWindowIcon(appWindow);

            if (ownerWindow == null)
            {
                return;
            }

            IntPtr ownerHandle = WindowNative.GetWindowHandle(ownerWindow);
            if (ownerHandle == IntPtr.Zero || windowHandle == IntPtr.Zero)
            {
                return;
            }

            ownerWindow.Closed += OwnerWindow_Closed;
            CenterNearOwner(ownerHandle, appWindow);
        }

        private void OwnerWindow_Closed(object sender, WindowEventArgs args)
        {
            if (!_isClosed)
            {
                Close();
            }
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

        private static void CenterNearOwner(IntPtr ownerHandle, AppWindow? appWindow)
        {
            if (appWindow == null || !GetWindowRect(ownerHandle, out Rect ownerRect))
            {
                return;
            }

            int ownerWidth = ownerRect.Right - ownerRect.Left;
            int ownerHeight = ownerRect.Bottom - ownerRect.Top;
            int x = ownerRect.Left + Math.Max(0, (ownerWidth - DefaultWindowWidth) / 2);
            int y = ownerRect.Top + Math.Max(0, (ownerHeight - DefaultWindowHeight) / 2);
            appWindow.Move(new PointInt32(x, y));
        }

        private static void SetWindowIcon(AppWindow? appWindow)
        {
            try
            {
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

        private void RefreshLogIfChanged()
        {
            IReadOnlyList<UiLogEntry> entries = _entryProvider();
            int count = entries.Count;
            string tail = count > 0 ? entries[count - 1].FormattedText : string.Empty;
            int signature = HashCode.Combine(count, tail);
            if (signature == _lastLogSignature)
            {
                return;
            }

            _lastLogSignature = signature;
            RefreshLog();
            QueueScrollToBottom();
        }

        private void RefreshLog()
        {
            LogViewer.Blocks.Clear();
            var paragraph = new Paragraph();
            foreach (UiLogEntry entry in _entryProvider())
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = AddStrictWrapPoints(entry.FormattedText),
                    Foreground = new SolidColorBrush(GetLogColor(entry.Level))
                });
                paragraph.Inlines.Add(new LineBreak());
            }

            LogViewer.Blocks.Add(paragraph);
        }

        private void QueueScrollToBottom()
        {
            LogScrollViewer.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                ScrollToBottom();
                LogScrollViewer.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ScrollToBottom);
            });
        }

        private void ScrollToBottom()
        {
            LogScrollViewer.UpdateLayout();
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null, true);
        }

        private static string AddStrictWrapPoints(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            const string zwsp = "\u200B";
            const int maxUnbrokenRun = 20;

            var sb = new System.Text.StringBuilder(text.Length + (text.Length / maxUnbrokenRun) + 8);
            int runLength = 0;

            foreach (char ch in text)
            {
                sb.Append(ch);

                if (ch == '\r' || ch == '\n' || char.IsWhiteSpace(ch))
                {
                    runLength = 0;
                    continue;
                }

                runLength++;

                if (ch is '\\' or '/' or '-' or '_' or '?' or '&' or '=' or ',' or '}' or ']' or '{' or '[' or ':')
                {
                    sb.Append(zwsp);
                    runLength = 0;
                    continue;
                }

                if (runLength >= maxUnbrokenRun)
                {
                    sb.Append(zwsp);
                    runLength = 0;
                }
            }

            return sb.ToString();
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private Windows.UI.Color GetLogColor(UiLogLevel level)
        {
            bool isDark = LogViewerRoot.ActualTheme == ElementTheme.Dark;
            return level switch
            {
                UiLogLevel.Tip => isDark
                    ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xD5, 0x8A)
                    : Windows.UI.Color.FromArgb(0xFF, 0x9A, 0x67, 0x00),
                UiLogLevel.Success => isDark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x8D, 0xE6, 0x9A)
                    : Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x6B, 0x2B),
                UiLogLevel.Error => isDark
                    ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x99, 0x99)
                    : Windows.UI.Color.FromArgb(0xFF, 0xB0, 0x00, 0x20),
                UiLogLevel.Ipatool => isDark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x9C, 0xC8, 0xFF)
                    : Windows.UI.Color.FromArgb(0xFF, 0x00, 0x5A, 0xB8),
                _ => isDark
                    ? Windows.UI.Color.FromArgb(0xFF, 0xE6, 0xE6, 0xE6)
                    : Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F)
            };
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
