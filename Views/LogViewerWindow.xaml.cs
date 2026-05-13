using IPAbuyer.Common;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace IPAbuyer.Views
{
    public sealed partial class LogViewerWindow : Window
    {
        private static readonly ResourceLoader Loader = new();

        private readonly IReadOnlyList<UiLogEntry> _entries;
        private readonly Func<UiLogLevel, Windows.UI.Color> _colorResolver;
        private readonly Action _onCopy;
        private readonly Action _onClear;
        private readonly DispatcherQueueTimer _refreshTimer;
        private readonly Window? _ownerWindow;
        private int _lastLogSignature = int.MinValue;
        private bool _hasStartedRefreshTimer;
        private bool _isClosed;
        private const int DefaultWindowWidth = 1100;
        private const int DefaultWindowHeight = 700;
        private const int GwlHwndParent = -8;

        public LogViewerWindow(
            IReadOnlyList<UiLogEntry> entries,
            Func<UiLogLevel, Windows.UI.Color> colorResolver,
            Action onCopy,
            Action onClear,
            Window? ownerWindow)
        {
            InitializeComponent();

            _entries = entries;
            _colorResolver = colorResolver;
            _onCopy = onCopy;
            _onClear = onClear;
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
            _onCopy();
            RefreshLog();
            QueueScrollToBottom();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _onClear();
            RefreshLog();
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

            _ = SetWindowLongPtr(windowHandle, GwlHwndParent, ownerHandle);
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
            int count = _entries.Count;
            string tail = count > 0 ? _entries[count - 1].FormattedText : string.Empty;
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
            foreach (UiLogEntry entry in _entries)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = AddStrictWrapPoints(entry.FormattedText),
                    Foreground = new SolidColorBrush(_colorResolver(entry.Level))
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

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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
