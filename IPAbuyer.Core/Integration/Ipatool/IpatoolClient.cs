using IPAbuyer.Core.Configuration;
using IPAbuyer.Core.Native;
using IPAbuyer.Core.Services.Downloads;
using IPAbuyer.Core.Services.Purchases;

namespace IPAbuyer.Core.Integration.Ipatool
{
    /// <summary>
    /// ipatool 操作的门面：命令执行已移入 Rust core（ipabuyer_core.dll），
    /// 这里只保留宿主侧的静态 API——认证查询/登出、payload 判定、
    /// 详细日志事件与关停钩子。页面代码继续按原有签名调用。
    /// </summary>
    public static class IpatoolClient
    {
        public static event Action<string>? CommandExecuting;
        public static event Action<string>? CommandOutputReceived;

        /// <summary>应用关停：请求取消 Core 侧所有在跑任务（同步、下载队列），
        /// Core 会在进程等待检查点终止子进程。对齐原 ProcessExecutionService.BeginShutdown 的尽力而为语义。</summary>
        public static void BeginShutdown()
        {
            PurchaseSyncService.Instance.CancelActive();
            DownloadQueueService.Instance.CancelActive();
        }

        /// <summary>查询登录状态。silent 为 true 时不上报详细日志事件（启动预热路径）。</summary>
        public static async Task<IpatoolResult> AuthInfoAsync(string? passphrase = null, CancellationToken cancellationToken = default, bool silent = false)
        {
            string executablePath = IpatoolPathResolver.ResolveExecutablePath();
            string resolvedPassphrase = ResolvePassphrase(passphrase);
            bool detailedLog = !silent && ApplicationSettings.GetDetailedIpatoolLogEnabled();

            CoreAuthInfo info = await Task.Run(() =>
            {
                using var cancel = new CoreCancelFlag();
                using CancellationTokenRegistration registration = cancellationToken.Register(cancel.Cancel);
                return CoreNative.AuthInfo(executablePath, resolvedPassphrase, detailedLog, cancel.Handle);
            }).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (!silent)
            {
                EmitCoreLogs(info.Logs);
            }

            // ExitCode 承载 Core 解析出的 is_success，供 IsSuccessResponse 判定。
            return new IpatoolResult(info.Payload, null, info.IsSuccess ? 0 : 1, false);
        }

        public static async Task<IpatoolResult> AuthLogoutAsync(CancellationToken cancellationToken = default)
        {
            string executablePath = IpatoolPathResolver.ResolveExecutablePath();
            bool detailedLog = ApplicationSettings.GetDetailedIpatoolLogEnabled();

            CoreLogoutResult logout = await Task.Run(() =>
            {
                using var cancel = new CoreCancelFlag();
                using CancellationTokenRegistration registration = cancellationToken.Register(cancel.Cancel);
                return CoreNative.AuthLogout(executablePath, detailedLog, cancel.Handle);
            }).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            EmitCoreLogs(logout.Logs);
            // 失败时的可读消息：取 ipatool 的输出行（已随 logs 透传）。
            string output = string.Join(
                Environment.NewLine,
                logout.Logs.Select(log => CoreMessages.Render(log.Message))
                    .Where(line => !string.IsNullOrWhiteSpace(line)));
            return new IpatoolResult(output, null, logout.Success ? 0 : 1, false);
        }

        public static string ExtractEmailFromPayload(string? payload) => IpatoolResponseParser.ExtractEmail(payload);

        public static bool IsPayloadSuccess(string? payload) => IpatoolResponseParser.IsSuccess(payload);

        public static bool HasExplicitFailureFlag(string? payload) => IpatoolResponseParser.HasExplicitFailure(payload);

        public static bool IsAccountMissingFromKeyring(string? payload) => IpatoolResponseParser.IsAccountMissingFromKeyring(payload);

        /// <summary>把 Core 返回的结构化日志透传到命令事件（详细日志开关已在 Core 侧生效）。
        /// "ipatool ..." 命令行走 CommandExecuting，其余输出行走 CommandOutputReceived，
        /// 对齐原命令执行器的两类事件。</summary>
        internal static void EmitCoreLogs(IEnumerable<CoreLogEntry> logs)
        {
            foreach (CoreLogEntry entry in logs)
            {
                string text = CoreMessages.Render(entry.Message);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (text.StartsWith("ipatool ", StringComparison.Ordinal))
                {
                    CommandExecuting?.Invoke(text);
                }
                else
                {
                    CommandOutputReceived?.Invoke(text);
                }
            }
        }

        /// <summary>密钥解析：显式传入优先，否则回退到 PassphraseStore（对齐原 IpatoolCommandBuilder.ResolvePassphrase）。</summary>
        internal static string ResolvePassphrase(string? passphrase)
        {
            if (!string.IsNullOrWhiteSpace(passphrase))
            {
                return passphrase.Trim();
            }

            return PassphraseStore.Get();
        }
    }
}
