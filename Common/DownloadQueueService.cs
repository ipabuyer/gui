using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IPAbuyer.Models;
using Microsoft.Windows.ApplicationModel.Resources;

namespace IPAbuyer.Common
{
    public enum DownloadQueueAddResult
    {
        Ignored = 0,
        Added = 1,
        Updated = 2,
        Requeued = 3
    }

    public sealed class DownloadQueueService
    {
        private static readonly ResourceLoader Loader = new();
        private static readonly Regex ProgressRegex = new(@"(?<!\d)(\d{1,3}(?:\.\d+)?)\s*[%％]", RegexOptions.Compiled);
        private static readonly Regex SuccessFlagRegex = new(@"success\s*[:=]\s*(true|false)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
        private const int ProgressBufferMaxLength = 256;
        private const int ProgressUiNotifyIntervalMs = 120;
        private readonly ObservableCollection<DownloadQueueItem> _items = new();
        private readonly SemaphoreSlim _runLock = new(1, 1);
        private CancellationTokenSource? _queueCts;
        private CancellationTokenSource? _currentItemCts;
        private bool _isRunning;

        private DownloadQueueService()
        {
        }

        public static DownloadQueueService Instance { get; } = new();

        public ObservableCollection<DownloadQueueItem> Items => _items;
        public bool IsRunning => _isRunning;

        public event Action<UiLogMessage>? LogReceived;
        public event Action? QueueChanged;

        public DownloadQueueAddResult AddOrUpdateFromSearchResult(SearchResult app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.bundleId))
            {
                return DownloadQueueAddResult.Ignored;
            }

            string bundleId = app.bundleId.Trim();
            var existing = _items.FirstOrDefault(i => i.BundleId == bundleId);
            if (existing != null)
            {
                existing.AppId = app.id ?? existing.AppId;
                existing.Name = app.name ?? existing.Name;
                existing.Developer = app.developer ?? existing.Developer;
                existing.Version = app.version ?? existing.Version;
                existing.Price = app.price ?? existing.Price;
                existing.ArtworkUrl = app.artworkUrl ?? existing.ArtworkUrl;

                bool requeued = existing.Status is DownloadQueueStatus.Failed or DownloadQueueStatus.Canceled or DownloadQueueStatus.Success;
                if (requeued)
                {
                    existing.Status = DownloadQueueStatus.Pending;
                    existing.LastMessage = L("DownloadQueue/Status/Requeued");
                }

                EmitLog(requeued
                    ? LF("DownloadQueue/Log/Requeued", existing.Name, existing.BundleId)
                    : LF("DownloadQueue/Log/Updated", existing.Name, existing.BundleId));
                NotifyQueueChanged();
                return requeued ? DownloadQueueAddResult.Requeued : DownloadQueueAddResult.Updated;
            }

            var item = new DownloadQueueItem
            {
                BundleId = bundleId,
                AppId = app.id ?? string.Empty,
                Name = app.name ?? bundleId,
                Developer = app.developer ?? string.Empty,
                Version = app.version ?? string.Empty,
                Price = app.price ?? string.Empty,
                ArtworkUrl = app.artworkUrl ?? string.Empty,
                Status = DownloadQueueStatus.Pending,
                LastMessage = L("DownloadQueue/Status/Pending")
            };

            _items.Add(item);
            EmitLog(LF("DownloadQueue/Log/Added", item.Name, item.BundleId));
            NotifyQueueChanged();
            return DownloadQueueAddResult.Added;
        }

        public int RemoveItems(System.Collections.Generic.IEnumerable<DownloadQueueItem> items)
        {
            if (items == null)
            {
                return 0;
            }

            var removing = items.ToList();
            int removed = 0;

            foreach (var item in removing)
            {
                if (_isRunning && item.Status == DownloadQueueStatus.Downloading)
                {
                    continue;
                }

                if (_items.Remove(item))
                {
                    removed++;
                }
            }

            if (removed > 0)
            {
                EmitLog(LF("DownloadQueue/Log/Removed", removed));
                NotifyQueueChanged();
            }

            return removed;
        }

        public async Task<int> StartQueueAsync()
        {
            await _runLock.WaitAsync();
            try
            {
                if (_isRunning)
                {
                    EmitLog(L("DownloadQueue/Log/AlreadyRunning"));
                    return 0;
                }

                int initialCount = CountRunnableItems();
                if (initialCount == 0)
                {
                    EmitLog(L("DownloadQueue/Log/NoPendingItems"));
                    return 0;
                }

                string account;
                try
                {
                    account = ResolveAccount();
                }
                catch (InvalidOperationException ex)
                {
                    EmitLog(ex.Message);
                    return 0;
                }

                _isRunning = true;
                _queueCts = new CancellationTokenSource();
                NotifyQueueChanged();

                string outputDirectory = KeychainConfig.GetDownloadDirectory();
                Directory.CreateDirectory(outputDirectory);
                bool useMockFlow = SessionState.IsLoggedIn
                    && SessionState.IsMockAccount
                    && string.Equals(SessionState.CurrentAccount, account, StringComparison.OrdinalIgnoreCase);

                EmitLog(LF("DownloadQueue/Log/StartQueue", initialCount, outputDirectory));

                int completed = 0;
                int processed = 0;
                var processedItems = new System.Collections.Generic.HashSet<DownloadQueueItem>();
                while (TryGetNextRunnableItem(processedItems, out var item))
                {
                    _queueCts.Token.ThrowIfCancellationRequested();
                    processed++;
                    processedItems.Add(item);

                    item.Status = DownloadQueueStatus.Downloading;
                    item.LastMessage = L("DownloadQueue/Status/Downloading");
                    NotifyQueueChanged();

                    _currentItemCts = CancellationTokenSource.CreateLinkedTokenSource(_queueCts.Token);
                    try
                    {
                        if (useMockFlow)
                        {
                            item.Status = DownloadQueueStatus.Success;
                            item.LastMessage = L("DownloadQueue/Status/Success");
                            completed++;
                            EmitLog(LF("DownloadQueue/Log/MockSuccess", item.Name));
                        }
                        else
                        {
                            string chunkLogBuffer = string.Empty;
                            string lastChunkLog = string.Empty;
                            int lastLoggedPercent = -1;
                            object chunkLogSync = new();
                            var result = await IpatoolExecution.DownloadAppWithProgressAsync(
                                item.BundleId,
                                outputDirectory,
                                account,
                                chunk =>
                                {
                                    lock (chunkLogSync)
                                    {
                                        EmitChunkLogLines(ref chunkLogBuffer, chunk, item.Name, ref lastLoggedPercent, ref lastChunkLog);
                                    }
                                },
                                _currentItemCts.Token);

                            lock (chunkLogSync)
                            {
                                EmitChunkLogLines(ref chunkLogBuffer, "\n", item.Name, ref lastLoggedPercent, ref lastChunkLog);
                            }

                            if (IsDownloadSuccess(result))
                            {
                                item.Status = DownloadQueueStatus.Success;
                                item.LastMessage = L("DownloadQueue/Status/Success");
                                completed++;
                                EmitLog(LF("DownloadQueue/Log/Success", item.Name));
                            }
                            else
                            {
                                string message = BuildErrorMessage(result);
                                item.Status = DownloadQueueStatus.Failed;
                                item.LastMessage = message;
                                EmitLog(LF("DownloadQueue/Log/Failed", item.Name, message));
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        item.Status = DownloadQueueStatus.Canceled;
                        item.LastMessage = L("DownloadQueue/Status/Canceled");
                        EmitLog(LF("DownloadQueue/Log/Canceled", item.Name));
                    }
                    catch (Exception ex)
                    {
                        item.Status = DownloadQueueStatus.Failed;
                        item.LastMessage = ex.Message;
                        EmitLog(LF("DownloadQueue/Log/Exception", item.Name, ex.Message));
                    }
                    finally
                    {
                        _currentItemCts?.Dispose();
                        _currentItemCts = null;
                        NotifyQueueChanged();
                    }
                }

                EmitLog(LF("DownloadQueue/Log/Completed", completed, processed));
                return completed;
            }
            catch (OperationCanceledException)
            {
                EmitLog(L("DownloadQueue/Log/QueueCanceled"));
                return 0;
            }
            finally
            {
                _isRunning = false;
                _queueCts?.Dispose();
                _queueCts = null;
                _currentItemCts?.Dispose();
                _currentItemCts = null;
                NotifyQueueChanged();
                _runLock.Release();
            }
        }

        public void CancelCurrent()
        {
            _currentItemCts?.Cancel();
            EmitLog(L("DownloadQueue/Log/CancelCurrentRequested"));
        }

        public void CancelAll()
        {
            _queueCts?.Cancel();
            _currentItemCts?.Cancel();

            foreach (var item in _items.Where(i => i.Status == DownloadQueueStatus.Pending))
            {
                item.Status = DownloadQueueStatus.Canceled;
                item.LastMessage = L("DownloadQueue/Status/QueueCanceled");
            }

            EmitLog(L("DownloadQueue/Log/CancelAllRequested"));
            NotifyQueueChanged();
        }

        private int CountRunnableItems()
        {
            return _items.Count(IsRunnableItem);
        }

        private bool TryGetNextRunnableItem(System.Collections.Generic.ISet<DownloadQueueItem> excludedItems, out DownloadQueueItem item)
        {
            DownloadQueueItem? nextItem = _items.FirstOrDefault(i => !excludedItems.Contains(i) && IsRunnableItem(i));
            if (nextItem == null)
            {
                item = null!;
                return false;
            }

            item = nextItem;
            return true;
        }

        private static bool IsRunnableItem(DownloadQueueItem item)
        {
            return item.Status == DownloadQueueStatus.Pending
                || item.Status == DownloadQueueStatus.Failed
                || item.Status == DownloadQueueStatus.Canceled;
        }

        private static bool IsDownloadSuccess(IpatoolExecution.IpatoolResult result)
        {
            string payload = result.OutputOrError;
            if (TryExtractSuccessFlag(payload, out bool successByText))
            {
                return successByText;
            }

            if (result.IsSuccessResponse)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            if (payload.Contains("\"success\":true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var token in JsonPayload.EnumerateTokens(payload))
            {
                if (JsonPayload.TryReadBoolean(token, "success", out bool success))
                {
                    return success;
                }
            }

            return false;
        }

        private static bool TryExtractSuccessFlag(string? payload, out bool success)
        {
            success = false;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            MatchCollection matches = SuccessFlagRegex.Matches(payload);
            if (matches.Count == 0)
            {
                return false;
            }

            Match match = matches[matches.Count - 1];
            if (match.Groups.Count < 2)
            {
                return false;
            }

            success = string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private static string BuildErrorMessage(IpatoolExecution.IpatoolResult result)
        {
            if (result.TimedOut)
            {
                return L("DownloadQueue/Error/Timeout");
            }

            string payload = result.OutputOrError;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return LF("DownloadQueue/Error/ExitCode", result.ExitCode);
            }

            foreach (var token in JsonPayload.EnumerateTokens(payload))
            {
                if (JsonPayload.TryReadString(token, out string? error, "error") && !string.IsNullOrWhiteSpace(error))
                {
                    return error;
                }

                if (JsonPayload.TryReadString(token, out string? message, "message") && !string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }

            return payload.Length > 160 ? payload.Substring(0, 160) + "..." : payload;
        }

        private static string ResolveAccount()
        {
            string account = SessionState.IsLoggedIn ? SessionState.CurrentAccount : string.Empty;
            if (string.IsNullOrWhiteSpace(account))
            {
                if (SessionState.IsLoggedIn)
                {
                    throw new InvalidOperationException(L("DownloadQueue/Error/MissingSessionEmail"));
                }

                throw new InvalidOperationException(L("DownloadQueue/Error/MissingAccount"));
            }

            return account.Trim();
        }

        private static int? TryExtractProgressPercent(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            line = SanitizeForParsing(line);
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            MatchCollection matches = ProgressRegex.Matches(line);
            if (matches.Count > 0)
            {
                Match match = matches[matches.Count - 1];
                if (match.Groups.Count >= 2 && TryConvertProgressValue(match.Groups[1].Value, out int percentBySymbol))
                {
                    return percentBySymbol;
                }
            }

            foreach (var token in JsonPayload.EnumerateTokens(line))
            {
                if (JsonPayload.TryReadString(
                    token,
                    out string? progressValue,
                    "progress",
                    "percent",
                    "percentage",
                    "completed",
                    "completion",
                    "fraction")
                    && TryConvertProgressValue(progressValue, out int percentByJson))
                {
                    return percentByJson;
                }
            }

            return null;
        }

        private static int? TryExtractProgressPercentFromChunk(ref string buffer, string? chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return null;
            }

            buffer += SanitizeForParsing(chunk);
            if (buffer.Length > ProgressBufferMaxLength)
            {
                buffer = buffer.Substring(buffer.Length - ProgressBufferMaxLength, ProgressBufferMaxLength);
            }

            return TryExtractProgressPercent(buffer);
        }

        private static bool ShouldNotifyProgress(int progress, ref long lastNotifyTick)
        {
            long nowTick = Environment.TickCount64;
            if (progress >= 100 || lastNotifyTick == 0 || nowTick - lastNotifyTick >= ProgressUiNotifyIntervalMs)
            {
                lastNotifyTick = nowTick;
                return true;
            }

            return false;
        }

        private void EmitChunkLogLines(ref string buffer, string? chunk, string appName, ref int lastLoggedPercent, ref string lastChunkLog)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            buffer += chunk;
            if (buffer.Length > 4096)
            {
                buffer = buffer.Substring(buffer.Length - 4096, 4096);
            }

            int lineBreakIndex;
            while ((lineBreakIndex = FindLineBreakIndex(buffer)) >= 0)
            {
                string line = buffer.Substring(0, lineBreakIndex);
                int consume = 1;
                if (lineBreakIndex + 1 < buffer.Length
                    && buffer[lineBreakIndex] == '\r'
                    && buffer[lineBreakIndex + 1] == '\n')
                {
                    consume = 2;
                }

                buffer = buffer.Substring(lineBreakIndex + consume);
                EmitSingleChunkLine(line, appName, ref lastLoggedPercent, ref lastChunkLog);
            }

            // 无换行时也周期性输出，避免“只有结束/取消才看见日志”。
            string pending = buffer.Trim();
            if (pending.Length >= 48)
            {
                EmitSingleChunkLine(pending, appName, ref lastLoggedPercent, ref lastChunkLog);
                buffer = string.Empty;
            }
        }

        private static int FindLineBreakIndex(string value)
        {
            int rn = value.IndexOf("\r\n", StringComparison.Ordinal);
            int r = value.IndexOf('\r');
            int n = value.IndexOf('\n');

            int first = -1;
            if (rn >= 0)
            {
                first = rn;
            }

            if (r >= 0 && (first < 0 || r < first))
            {
                first = r;
            }

            if (n >= 0 && (first < 0 || n < first))
            {
                first = n;
            }

            return first;
        }

        private void EmitSingleChunkLine(string rawLine, string appName, ref int lastLoggedPercent, ref string lastChunkLog)
        {
            string line = SanitizeForParsing(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            int? percent = TryExtractProgressPercent(line);
            if (percent.HasValue)
            {
                if (percent.Value == lastLoggedPercent)
                {
                    return;
                }

                lastLoggedPercent = percent.Value;
                EmitUniqueChunkLog(appName, line, ref lastChunkLog);
                return;
            }

            EmitUniqueChunkLog(appName, line, ref lastChunkLog);
        }

        private void EmitUniqueChunkLog(string appName, string line, ref string lastChunkLog)
        {
            if (!KeychainConfig.GetDetailedIpatoolLogEnabled())
            {
                return;
            }

            if (string.Equals(lastChunkLog, line, StringComparison.Ordinal))
            {
                return;
            }

            lastChunkLog = line;
            EmitLog($"[{appName}] {line}", UiLogSource.Ipatool);
        }

        private static string SanitizeForParsing(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            string withoutAnsi = AnsiEscapeRegex.Replace(input, string.Empty);
            var chars = new char[withoutAnsi.Length];
            int index = 0;

            foreach (char ch in withoutAnsi)
            {
                if (ch == '\r' || ch == '\n' || ch == '\t' || !char.IsControl(ch))
                {
                    chars[index++] = ch;
                }
            }

            return index == 0 ? string.Empty : new string(chars, 0, index);
        }

        private static bool TryConvertProgressValue(string raw, out int percent)
        {
            percent = 0;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return false;
            }

            // JSON 中常见 0~1 比例值，这里统一转成百分比。
            if (value >= 0d && value <= 1d)
            {
                value *= 100d;
            }

            percent = Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
            return true;
        }

        private void EmitLog(string message, UiLogSource source = UiLogSource.App)
        {
            LogReceived?.Invoke(new UiLogMessage(message, source));
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private static string LF(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key), args);
        }

        private void NotifyQueueChanged()
        {
            QueueChanged?.Invoke();
        }
    }
}
