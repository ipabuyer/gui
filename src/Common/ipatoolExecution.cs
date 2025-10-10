using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IPAbuyer.Common
{
    public static class ipatoolExecution
    {
        private const int MaxPreviewLength = 200;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

        public sealed record IpatoolResult(string? Output, string? Error, int ExitCode, bool TimedOut)
        {
            public string OutputOrError => string.IsNullOrWhiteSpace(Output) ? Error ?? string.Empty : Output;
            public bool IsSuccessResponse => !TimedOut && ExitCode == 0;
        }

        public static Task<IpatoolResult> AuthLoginAsync(string account, string password, string authCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                throw new ArgumentException("account 不能为空", nameof(account));
            }

            string arguments = $"auth login --email \"{account}\" --password \"{password}\" --auth-code \"{authCode}\"";
            return ExecuteIpatoolAsync(arguments, account, cancellationToken);
        }

        public static Task<IpatoolResult> AuthLogoutAsync(string account, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                throw new ArgumentException("account 不能为空", nameof(account));
            }

            return ExecuteIpatoolAsync("auth revoke", account, cancellationToken);
        }

        public static Task<IpatoolResult> SearchAppAsync(string name, int limit, string account, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                throw new ArgumentException("account 不能为空", nameof(account));
            }

            string safeName = name?.Trim() ?? string.Empty;
            if (safeName.Length == 0)
            {
                throw new ArgumentException("应用名称不能为空", nameof(name));
            }

            string arguments = $"search \"{safeName}\" --limit {Math.Max(1, limit)}";
            return ExecuteIpatoolAsync(arguments, account, cancellationToken);
        }

        public static Task<IpatoolResult> AuthInfoAsync(string account, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                throw new ArgumentException("account 不能为空", nameof(account));
            }

            return ExecuteIpatoolAsync("auth info", account, cancellationToken);
        }

        public static Task<IpatoolResult> PurchaseAppAsync(string bundleId, string account, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                throw new ArgumentException("account 不能为空", nameof(account));
            }

            if (string.IsNullOrWhiteSpace(bundleId))
            {
                throw new ArgumentException("bundleId 不能为空", nameof(bundleId));
            }

            string arguments = $"purchase --bundle-identifier \"{bundleId}\"";
            return ExecuteIpatoolAsync(arguments, account, cancellationToken);
        }

        private static async Task<IpatoolResult> ExecuteIpatoolAsync(string arguments, string account, CancellationToken cancellationToken)
        {
            bool isLogout = arguments.TrimStart().StartsWith("auth revoke", StringComparison.OrdinalIgnoreCase);

            string ipatoolPath = ResolveIpatoolPath();
            string workingDirectory = Path.GetDirectoryName(ipatoolPath) ?? AppContext.BaseDirectory;

            string passphrase = EnsurePassphrase(account);

            var psi = new ProcessStartInfo
            {
                FileName = ipatoolPath,
                Arguments = $"{arguments} --keychain-passphrase {passphrase} --format json --non-interactive --verbose",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workingDirectory,
            };

            psi.EnvironmentVariables["NO_COLOR"] = "1";
            psi.EnvironmentVariables["TERM"] = "dumb";

            Process? process = null;

            try
            {
                if (isLogout)
                {
                    DeleteCookieLockFile();
                }

                process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                if (!process.Start())
                {
                    return new IpatoolResult(null, "无法启动 ipatool 进程", ExitCode: -1, TimedOut: false);
                }

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linkedCts.CancelAfter(DefaultTimeout);

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                try
                {
                    await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    TryTerminateProcess(process);
                    return new IpatoolResult(null, $"执行超时: {arguments}", ExitCode: -1, TimedOut: true);
                }

                string output = await outputTask.ConfigureAwait(false);
                string error = await errorTask.ConfigureAwait(false);

                Debug.WriteLine($"ipatool output: {Preview(output)}");
                Debug.WriteLine($"ipatool stderr: {Preview(error)}");

                string? normalizedOutput = ExtractMeaningfulJson(output);
                string? normalizedError = ExtractMeaningfulJson(error) ?? error;

                // 如果标准输出没有有用信息，返回标准错误中的内容
                if (string.IsNullOrWhiteSpace(normalizedOutput) && !string.IsNullOrWhiteSpace(normalizedError))
                {
                    return new IpatoolResult(normalizedError, normalizedError, process.ExitCode, TimedOut: false);
                }

                return new IpatoolResult(normalizedOutput ?? output, normalizedError, process.ExitCode, TimedOut: false);
            }
            catch (Exception ex)
            {
                if (process != null)
                {
                    TryTerminateProcess(process);
                }
                return new IpatoolResult(null, ex.Message, -1, TimedOut: false);
            }
            finally
            {
                process?.Dispose();

                if (isLogout)
                {
                    DeleteCookieLockFile();
                }
            }
        }

        private static string ResolveIpatoolPath()
        {
            string baseDirectory = AppContext.BaseDirectory;
            string defaultPath = Path.Combine(baseDirectory, "ipatool.exe");
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }

            string includeDirectory = Path.Combine(baseDirectory, "Include");
            if (Directory.Exists(includeDirectory))
            {
                string binaryName = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => "ipatool-2.2.0-windows-arm64.exe",
                    Architecture.X64 => "ipatool-2.2.0-windows-amd64.exe",
                    _ => "ipatool.exe"
                };

                string candidate = Path.Combine(includeDirectory, binaryName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            Debug.WriteLine("未在应用目录中找到专用 ipatool，可执行文件将从 PATH 中解析。");
            return "ipatool.exe";
        }

        private static string EnsurePassphrase(string account)
        {
            string? existingKey = KeychainConfig.GetSecretKey(account);
            if (!string.IsNullOrEmpty(existingKey))
            {
                return existingKey;
            }

            return KeychainConfig.GenerateAndSaveSecretKey(account);
        }

        private static void TryTerminateProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // 忽略终止过程中可能发生的任何异常
            }
        }

        private static string? ExtractMeaningfulJson(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            string trimmed = content.Trim();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            {
                return trimmed;
            }

            string[] lines = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string candidate = line.Trim();
                if (candidate.StartsWith("{") || candidate.StartsWith("["))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string Preview(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length <= MaxPreviewLength ? value : value.Substring(0, MaxPreviewLength) + "...";
        }

        private static void DeleteCookieLockFile()
        {
            try
            {
                string lockPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ipatool", "cookies.lock");
                if (File.Exists(lockPath))
                {
                    File.Delete(lockPath);
                    Debug.WriteLine($"删除 ipatool cookies.lock: {lockPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"删除 ipatool cookies.lock 失败: {ex.Message}");
            }
        }
    }
}
