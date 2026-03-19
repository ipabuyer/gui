using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IPAbuyer.Models;

namespace IPAbuyer.Common
{
    public sealed class DownloadQueueService
    {
        private static readonly Regex ProgressRegex = new(@"(?<!\d)(\d{1,3}(?:\.\d+)?)\s*[%％]", RegexOptions.Compiled);
        private static readonly Regex JsonProgressRegex = new(@"""(?:progress|percent|percentage|completed|completion|fraction)""\s*:\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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

        public void AddOrUpdateFromSearchResult(SearchResult app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.bundleId))
            {
                return;
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

                if (existing.Status is DownloadQueueStatus.Failed or DownloadQueueStatus.Canceled or DownloadQueueStatus.Success)
                {
                    existing.Status = DownloadQueueStatus.Pending;
                    existing.LastMessage = "已重新加入下载队列";
                }

                EmitLog($"队列更新: {existing.Name} ({existing.BundleId})");
                NotifyQueueChanged();
                return;
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
                LastMessage = "等待下载"
            };

            _items.Add(item);
            EmitLog($"已加入下载队列: {item.Name} ({item.BundleId})");
            NotifyQueueChanged();
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
                EmitLog($"已移出下载队列: {removed} 项");
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
                    EmitLog("下载队列已在运行");
                    return 0;
                }

                var candidates = _items
                    .Where(i => i.Status == DownloadQueueStatus.Pending
                        || i.Status == DownloadQueueStatus.Failed
                        || i.Status == DownloadQueueStatus.Canceled)
                    .ToList();
                if (candidates.Count == 0)
                {
                    EmitLog("没有待下载项目");
                    return 0;
                }

                _isRunning = true;
                _queueCts = new CancellationTokenSource();
                NotifyQueueChanged();

                string account = ResolveAccount();
                string outputDirectory = KeychainConfig.GetDownloadDirectory();
                Directory.CreateDirectory(outputDirectory);
                bool useMockFlow = SessionState.IsLoggedIn
                    && SessionState.IsMockAccount
                    && string.Equals(SessionState.CurrentAccount, account, StringComparison.OrdinalIgnoreCase);

                EmitLog($"开始下载队列，共 {candidates.Count} 项，输出目录: {outputDirectory}");

                int completed = 0;
                foreach (var item in candidates)
                {
                    _queueCts.Token.ThrowIfCancellationRequested();

                    item.Status = DownloadQueueStatus.Downloading;
                    item.LastMessage = "下载中";
                    NotifyQueueChanged();

                    _currentItemCts = CancellationTokenSource.CreateLinkedTokenSource(_queueCts.Token);
                    try
                    {
                        if (useMockFlow)
                        {
                            item.Status = DownloadQueueStatus.Success;
                            item.LastMessage = "下载成功";
                            completed++;
                            EmitLog($"下载成功(测试账户): {item.Name}");
                        }
                        else
                        {
                            string chunkLogBuffer = string.Empty;
                            string lastChunkLog = string.Empty;
                            int lastLoggedPercent = -1;
                            var result = await IpatoolExecution.DownloadAppWithProgressAsync(
                                item.BundleId,
                                outputDirectory,
                                account,
                                chunk =>
                                {
                                    EmitChunkLogLines(ref chunkLogBuffer, chunk, item.Name, ref lastLoggedPercent, ref lastChunkLog);
                                },
                                _currentItemCts.Token);

                            EmitChunkLogLines(ref chunkLogBuffer, "\n", item.Name, ref lastLoggedPercent, ref lastChunkLog);

                            if (IsDownloadSuccess(result))
                            {
                                item.Status = DownloadQueueStatus.Success;
                                item.LastMessage = "下载成功";
                                completed++;
                                EmitLog($"下载成功: {item.Name}");
                            }
                            else
                            {
                                string message = BuildErrorMessage(result);
                                item.Status = DownloadQueueStatus.Failed;
                                item.LastMessage = message;
                                EmitLog($"下载失败: {item.Name} - {message}");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        item.Status = DownloadQueueStatus.Canceled;
                        item.LastMessage = "下载已终止";
                        EmitLog($"下载已终止: {item.Name}");
                    }
                    catch (Exception ex)
                    {
                        item.Status = DownloadQueueStatus.Failed;
                        item.LastMessage = ex.Message;
                        EmitLog($"下载异常: {item.Name} - {ex.Message}");
                    }
                    finally
                    {
                        _currentItemCts?.Dispose();
                        _currentItemCts = null;
                        NotifyQueueChanged();
                    }
                }

                EmitLog($"下载队列完成，成功 {completed}/{candidates.Count}");
                return completed;
            }
            catch (OperationCanceledException)
            {
                EmitLog("下载队列已终止");
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
            EmitLog("已请求终止当前下载任务");
        }

        public void CancelAll()
        {
            _queueCts?.Cancel();
            _currentItemCts?.Cancel();

            foreach (var item in _items.Where(i => i.Status == DownloadQueueStatus.Pending))
            {
                item.Status = DownloadQueueStatus.Canceled;
                item.LastMessage = "队列已终止";
            }

            EmitLog("已请求终止所有下载任务");
            NotifyQueueChanged();
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

            foreach (string segment in EnumerateJsonSegments(payload))
            {
                try
                {
                    using var doc = JsonDocument.Parse(segment);
                    if (doc.RootElement.TryGetProperty("success", out var success))
                    {
                        return success.ValueKind == JsonValueKind.True
                            || (success.ValueKind == JsonValueKind.String
                                && string.Equals(success.GetString(), "true", StringComparison.OrdinalIgnoreCase));
                    }
                }
                catch (JsonException)
                {
                    // ignore and continue trying remaining segments
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
                return "下载超时";
            }

            string payload = result.OutputOrError;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return $"退出码 {result.ExitCode}";
            }

            foreach (string segment in EnumerateJsonSegments(payload))
            {
                try
                {
                    using var doc = JsonDocument.Parse(segment);
                    if (doc.RootElement.TryGetProperty("error", out var error))
                    {
                        return error.GetString() ?? payload;
                    }

                    if (doc.RootElement.TryGetProperty("message", out var message))
                    {
                        return message.GetString() ?? payload;
                    }
                }
                catch (JsonException)
                {
                    // ignore and continue trying remaining segments
                }
            }

            return payload.Length > 160 ? payload.Substring(0, 160) + "..." : payload;
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateJsonSegments(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                yield break;
            }

            string normalized = payload.Replace("}{", "}\n{");
            string[] lines = normalized.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    yield return trimmed;
                }
            }

            if (lines.Length == 0)
            {
                string trimmed = payload.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    yield return trimmed;
                }
            }
        }

        private static string ResolveAccount()
        {
            string account = SessionState.IsLoggedIn ? SessionState.CurrentAccount : string.Empty;
            if (string.IsNullOrWhiteSpace(account))
            {
                account = KeychainConfig.GetLastLoginUsername() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(account))
            {
                throw new InvalidOperationException("未找到可用账号，请先登录");
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

            MatchCollection jsonMatches = JsonProgressRegex.Matches(line);
            if (jsonMatches.Count == 0)
            {
                return null;
            }

            Match jsonMatch = jsonMatches[jsonMatches.Count - 1];
            if (jsonMatch.Groups.Count < 2 || !TryConvertProgressValue(jsonMatch.Groups[1].Value, out int percentByJson))
            {
                return null;
            }

            return percentByJson;
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

        private void NotifyQueueChanged()
        {
            QueueChanged?.Invoke();
        }
    }
}
