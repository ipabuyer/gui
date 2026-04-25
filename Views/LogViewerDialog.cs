using System;
using System.Collections.Generic;
using IPAbuyer.Common;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace IPAbuyer.Views
{
    public sealed class LogViewerDialog : ContentDialog
    {
        private static readonly ResourceLoader Loader = new();

        private readonly IReadOnlyList<UiLogEntry> _entries;
        private readonly Func<UiLogLevel, Windows.UI.Color> _colorResolver;
        private readonly Action _onCopy;
        private readonly Action _onClear;
        private readonly RichTextBlock _logViewer;
        private readonly ScrollViewer _logScrollViewer;
        private readonly DispatcherQueueTimer _refreshTimer;
        private int _lastLogSignature = int.MinValue;
        private const double FooterButtonWidth = 132;

        public LogViewerDialog(
            IReadOnlyList<UiLogEntry> entries,
            Func<UiLogLevel, Windows.UI.Color> colorResolver,
            Action onCopy,
            Action onClear,
            XamlRoot xamlRoot)
        {
            _entries = entries;
            _colorResolver = colorResolver;
            _onCopy = onCopy;
            _onClear = onClear;

            double dialogWidth = Math.Clamp(xamlRoot.Size.Width * 0.8, 980, 1440);
            double dialogHeight = Math.Clamp(xamlRoot.Size.Height * 0.8, 560, 920);
            double contentHeight = Math.Max(420, dialogHeight - 130);
            Resources["ContentDialogMinWidth"] = dialogWidth;
            Resources["ContentDialogMaxWidth"] = dialogWidth;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;

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

            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };

            var closeButton = new Button
            {
                Content = L("Common/LogDialog/CloseButton"),
                MinHeight = 32,
                Width = FooterButtonWidth,
                Padding = new Thickness(12, 0, 12, 0)
            };
            closeButton.Click += (_, _) => Hide();

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
            Content = new StackPanel
            {
                Spacing = 0,
                Height = contentHeight,
                Children =
                {
                    header,
                    new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x12, 0x12, 0x12)),
                        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(12),
                        Height = Math.Max(320, contentHeight - 54),
                        Child = _logScrollViewer
                    },
                    footer
                }
            };
            CloseButtonText = string.Empty;
            XamlRoot = xamlRoot;
            Opened += (_, _) =>
            {
                _lastLogSignature = int.MinValue;
                RefreshLogIfChanged();
                QueueScrollToBottom();
                _refreshTimer.Start();
            };
            Closed += (_, _) =>
            {
                _refreshTimer.Stop();
            };

            RefreshLog();
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
    }
}
