using IPAbuyer.Core.Execution;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Diagnostics;

namespace IPAbuyer.Core.Integration.Ipatool
{
    public static class IpatoolClient
    {
        private static readonly ResourceLoader Loader = new();
        private static readonly ProcessExecutionService ProcessExecutionService = new();
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

        // 登录最多包含两次认证尝试，正常耗时以秒计；缩短超时以限制异常情况下界面的无响应时长。
        private static readonly TimeSpan AuthLoginTimeout = TimeSpan.FromSeconds(60);

        // 下载大体积 App 可能远超常规命令耗时，不设固定超时，由“终止下载”或应用关闭终止进程。
        private static readonly TimeSpan DownloadTimeout = System.Threading.Timeout.InfiniteTimeSpan;

        public static event Action<string>? CommandExecuting;
        public static event Action<string>? CommandOutputReceived;

        public static void BeginShutdown()
        {
            ProcessExecutionService.BeginShutdown();
        }

        public static Task<IpatoolResult> AuthLoginAsync(string account, string password, string authCode, string passphrase, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                throw new ArgumentException(LF("Ipatool/Error/RequiredArgument", "account"), nameof(account));
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException(LF("Ipatool/Error/RequiredArgument", "password"), nameof(password));
            }

            var arguments = new List<string> { "auth", "login", "--email", account, "--password", password };
            if (!string.IsNullOrWhiteSpace(authCode))
            {
                arguments.Add("--auth-code");
                arguments.Add(authCode);
            }

            return ExecuteAsync(arguments, passphrase, cancellationToken, timeout: AuthLoginTimeout);
        }

        public static Task<IpatoolResult> AuthLogoutAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(new[] { "auth", "revoke" }, null, cancellationToken);
        }

        public static Task<IpatoolResult> AuthInfoAsync(string? passphrase = null, CancellationToken cancellationToken = default, bool silent = false)
        {
            return ExecuteAsync(new[] { "auth", "info" }, passphrase, cancellationToken, silent);
        }

        public static Task<IpatoolResult> ListPurchasesAsync(int maxResults, int page, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                IpatoolCommandBuilder.BuildListPurchasesArguments(maxResults, page),
                passphrase: null,
                cancellationToken);
        }

        public static Task<IpatoolResult> PurchaseAppAsync(string bundleId, string account, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                throw new ArgumentException(LF("Ipatool/Error/RequiredArgument", "account"), nameof(account));
            }

            if (string.IsNullOrWhiteSpace(bundleId))
            {
                throw new ArgumentException(LF("Ipatool/Error/RequiredArgument", "bundleId"), nameof(bundleId));
            }

            return ExecuteAsync(new[] { "purchase", "--bundle-identifier", bundleId }, null, cancellationToken);
        }

        public static Task<IpatoolResult> DownloadAppAsync(string bundleId, string outputDirectory, string account, CancellationToken cancellationToken = default)
        {
            return DownloadAppWithProgressAsync(bundleId, outputDirectory, account, null, cancellationToken);
        }

        public static async Task<IpatoolResult> DownloadAppWithProgressAsync(
            string bundleId,
            string outputDirectory,
            string account,
            Action<string>? outputChunkCallback,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                throw new ArgumentException(LF("Ipatool/Error/RequiredArgument", "account"), nameof(account));
            }

            if (string.IsNullOrWhiteSpace(bundleId))
            {
                throw new ArgumentException(LF("Ipatool/Error/RequiredArgument", "bundleId"), nameof(bundleId));
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException(LF("Ipatool/Error/RequiredArgument", "outputDirectory"), nameof(outputDirectory));
            }

            Directory.CreateDirectory(outputDirectory);
            string executablePath = IpatoolPathResolver.ResolveExecutablePath();
            IReadOnlyList<string> arguments = IpatoolCommandBuilder.BuildDownloadArguments(
                bundleId,
                outputDirectory,
                IpatoolCommandBuilder.ResolvePassphrase(null));

            try
            {
                IpatoolCommandLog.EmitCommandIfEnabled(arguments, CommandExecuting);
                var request = new ProcessExecutionRequest(
                    executablePath,
                    IpatoolPathResolver.GetWorkingDirectory(executablePath),
                    arguments,
                    DownloadTimeout,
                    IpatoolCommandBuilder.CreateEnvironmentVariables(),
                    outputChunkCallback);
                ProcessExecutionResult result = await ProcessExecutionService.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
                if (result.TimedOut)
                {
                    return new IpatoolResult(null, LF("Ipatool/Error/ExecutionTimeout", $"download --bundle-identifier {bundleId}"), -1, true);
                }

                if (outputChunkCallback == null)
                {
                    IpatoolCommandLog.EmitOutputIfEnabled(result.StandardOutput, result.StandardError, CommandOutputReceived);
                }

                Debug.WriteLine($"ipatool output: {IpatoolCommandLog.Preview(result.StandardOutput)}");
                Debug.WriteLine($"ipatool stderr: {IpatoolCommandLog.Preview(result.StandardError)}");
                (string output, string error) = IpatoolResponseParser.NormalizeStreams(result.StandardOutput, result.StandardError, result.ExitCode);
                return new IpatoolResult(output, error, result.ExitCode, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new IpatoolResult(null, ex.Message, -1, false);
            }
        }

        public static string ExtractEmailFromPayload(string? payload) => IpatoolResponseParser.ExtractEmail(payload);

        public static bool IsPayloadSuccess(string? payload) => IpatoolResponseParser.IsSuccess(payload);

        public static bool HasExplicitFailureFlag(string? payload) => IpatoolResponseParser.HasExplicitFailure(payload);

        public static bool IsAccountMissingFromKeyring(string? payload) => IpatoolResponseParser.IsAccountMissingFromKeyring(payload);

        private static async Task<IpatoolResult> ExecuteAsync(
            IReadOnlyList<string> commandArguments,
            string? passphrase,
            CancellationToken cancellationToken,
            bool suppressLogEvents = false,
            TimeSpan? timeout = null)
        {
            bool isLogout = IpatoolCommandBuilder.IsLogout(commandArguments);
            string executablePath = IpatoolPathResolver.ResolveExecutablePath();
            IReadOnlyList<string> arguments = IpatoolCommandBuilder.BuildStandardArguments(
                commandArguments,
                IpatoolCommandBuilder.ResolvePassphrase(passphrase),
                isLogout);

            try
            {
                if (isLogout)
                {
                    IpatoolPathResolver.DeleteCookieLockFile();
                }

                if (!suppressLogEvents)
                {
                    IpatoolCommandLog.EmitCommandIfEnabled(arguments, CommandExecuting);
                }

                var request = new ProcessExecutionRequest(
                    executablePath,
                    IpatoolPathResolver.GetWorkingDirectory(executablePath),
                    arguments,
                    timeout ?? DefaultTimeout,
                    IpatoolCommandBuilder.CreateEnvironmentVariables());
                ProcessExecutionResult result = await ProcessExecutionService.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
                if (result.TimedOut)
                {
                    return new IpatoolResult(null, LF("Ipatool/Error/ExecutionTimeout", IpatoolCommandBuilder.GetSafeCommandLabel(commandArguments)), -1, true);
                }

                if (!suppressLogEvents)
                {
                    IpatoolCommandLog.EmitOutputIfEnabled(result.StandardOutput, result.StandardError, CommandOutputReceived);
                }

                Debug.WriteLine($"ipatool output: {IpatoolCommandLog.Preview(result.StandardOutput)}");
                Debug.WriteLine($"ipatool stderr: {IpatoolCommandLog.Preview(result.StandardError)}");
                (string output, string error) = IpatoolResponseParser.NormalizeStreams(result.StandardOutput, result.StandardError, result.ExitCode);
                return new IpatoolResult(output, error, result.ExitCode, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new IpatoolResult(null, ex.Message, -1, false);
            }
            finally
            {
                if (isLogout)
                {
                    IpatoolPathResolver.DeleteCookieLockFile();
                }
            }
        }

        private static string LF(string key, params object[] args)
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, Loader.GetString(key), args);
        }
    }
}
