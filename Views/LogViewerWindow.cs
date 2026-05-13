using IPAbuyer.Common;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace IPAbuyer.Views
{
    public sealed class LogViewerWindow : Window
    {
        private static readonly ResourceLoader Loader = new();

        private readonly IReadOnlyList<UiLogEntry> _entries;
        private readonly Func<UiLogLevel, Windows.UI.Color> _colorResolver;
        private readonly Action _onCopy;
        private readonly Action _onClear;
        private readonly RichTextBlock _logViewer;
        private readonly ScrollViewer _logScrollViewer;
        private readonly DispatcherQueueTimer _refreshTimer;
        private readonly Window? _ownerWindow;
        private int _lastLogSignature = int.MinValue;
        private bool _hasStartedRefreshTimer;
        private bool _isClosed;
        private const double FooterButtonWidth = 132;
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
            _entries = entries;
            _colorResolver = colorResolver;
            _onCopy = onCopy;
            _onClear = onClear;
            _ownerWindow = ownerWindow;

            _logViewer = new RichTextBlock
            {
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 13
            };
            _logScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _logViewer
            };
            _refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(250);
            _refreshTimer.Tick += (_, _) => RefreshLogIfChanged();

            var copyButton = new Button
            {
                Content = L("Common/LogDialog/CopyButton"),
                MinHeight = 32,
                Width = FooterButtonWidth,
                Padding = new Thickness(12, 0, 12, 0)
            };
            copyButton.Click += (_, _) =>
            {
                _onCopy();
                RefreshLog();
                QueueScrollToBottom();
            };

            var clearButton = new Button
            {
                Content = L("Common/LogDialog/ClearButton"),
                MinHeight = 32,
                Width = FooterButtonWidth,
                Padding = new Thickness(12, 0, 12, 0)
            };
            clearButton.Click += (_, _) =>
            {
                _onClear();
                RefreshLog();
            };

            var closeButton = new Button
            {
                Content = L("Common/LogDialog/CloseButton"),
                MinHeight = 32,
                Width = FooterButtonWidth,
                Padding = new Thickness(12, 0, 12, 0)
            };
            closeButton.Click += (_, _) => Close();

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0),
                Children =
                {
                    copyButton,
                    clearButton,
                    closeButton
                }
            };

            Title = L("Common/LogDialog/Title");
            ConfigureSystemBackdrop();
            Content = new Grid
            {
                Padding = new Thickness(16),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00)),
                Children =
                {
                    new Grid
                    {
                        RowDefinitions =
                        {
                            new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                            new RowDefinition { Height = GridLength.Auto }
                        },
                        Children =
                        {
                            new Border
                            {
                                CornerRadius = new CornerRadius(8),
                                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x12, 0x12, 0x12)),
                                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A)),
                                BorderThickness = new Thickness(1),
                                Padding = new Thickness(12),
                                Child = _logScrollViewer
                            },
                            footer
                        }
                    }
                }
            };
            Grid.SetRow(footer, 1);

            ConfigureWindow(ownerWindow);
            Activated += (_, _) =>
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
            };
            Closed += (_, _) =>
            {
                _isClosed = true;
                _refreshTimer.Stop();
                if (_ownerWindow != null)
                {
                    _ownerWindow.Closed -= OwnerWindow_Closed;
                }
            };

            RefreshLog();
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
            _logViewer.Blocks.Clear();
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

            _logViewer.Blocks.Add(paragraph);
        }

        private void QueueScrollToBottom()
        {
            _logScrollViewer.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                ScrollToBottom();
                _logScrollViewer.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ScrollToBottom);
            });
        }

        private void ScrollToBottom()
        {
            _logScrollViewer.UpdateLayout();
            _logScrollViewer.ChangeView(null, _logScrollViewer.ScrollableHeight, null, true);
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
