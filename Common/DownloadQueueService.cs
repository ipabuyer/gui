using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
        private static readonly Regex ProgressRegex = new(@"(?<!\d)(\d{1,3})(?:\.\d+)?\s*%", RegexOptions.Compiled);
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

        public event Action<string>? LogReceived;
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

                var pending = _items.Where(i => i.Status == DownloadQueueStatus.Pending).ToList();
                if (pending.Count == 0)
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

                EmitLog($"开始下载队列，共 {pending.Count} 项，输出目录: {outputDirectory}");

                int completed = 0;
                foreach (var item in pending)
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
                            int lastProgress = -1;
                            long lastNotifyTick = 0;
                            string progressBuffer = string.Empty;
                            var result = await IpatoolExecution.DownloadAppWithProgressAsync(
                                item.BundleId,
                                outputDirectory,
                                account,
                                chunk =>
                                {
                                    int? progress = TryExtractProgressPercentFromChunk(ref progressBuffer, chunk);
                                    if (!progress.HasValue || progress.Value == lastProgress)
                                    {
                                        return;
                                    }

                                    lastProgress = progress.Value;
                                    item.LastMessage = $"下载中 {lastProgress}%";
                                    if (ShouldNotifyProgress(lastProgress, ref lastNotifyTick))
                                    {
                                        NotifyQueueChanged();
                                    }
                                },
                                _currentItemCts.Token);

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

                EmitLog($"下载队列完成，成功 {completed}/{pending.Count}");
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
            if (result.IsSuccessResponse)
            {
                return true;
            }

            string payload = result.OutputOrError;
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

            MatchCollection matches = ProgressRegex.Matches(line);
            if (matches.Count == 0)
            {
                return null;
            }

            Match match = matches[matches.Count - 1];
            if (match.Groups.Count < 2 || !int.TryParse(match.Groups[1].Value, out int percent))
            {
                return null;
            }

            return Math.Clamp(percent, 0, 100);
        }

        private static int? TryExtractProgressPercentFromChunk(ref string buffer, string? chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return null;
            }

            buffer += chunk;
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

        private void EmitLog(string message)
        {
            string log = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogReceived?.Invoke(log);
        }

        private void NotifyQueueChanged()
        {
            QueueChanged?.Invoke();
        }
    }
}
