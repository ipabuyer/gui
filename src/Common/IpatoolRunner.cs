using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IPAbuyer.Common
{
    /// <summary>
    /// 统一管理 ipatool 可执行文件查找与调用的逻辑，集中处理超时、日志与工作目录问题。
    /// </summary>
    public static class IpatoolRunner
    {
        private static string? _cachedPath;

        public static string GetIpatoolPath()
        {
            if (!string.IsNullOrEmpty(_cachedPath))
                return _cachedPath;

            try
            {
                string baseDirectory = AppContext.BaseDirectory;

                string includePath = Path.Combine(baseDirectory, "Include", "ipatool.exe");
                if (File.Exists(includePath))
                {
                    _cachedPath = includePath;
                    Debug.WriteLine($"IpatoolRunner: found in Include: {_cachedPath}");
                    return _cachedPath;
                }

                string currentDirPath = Path.Combine(baseDirectory, "ipatool.exe");
                if (File.Exists(currentDirPath))
                {
                    _cachedPath = currentDirPath;
                    Debug.WriteLine($"IpatoolRunner: found in base dir: {_cachedPath}");
                    return _cachedPath;
                }

                try
                {
                    var found = Directory.EnumerateFiles(baseDirectory, "ipatool.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrEmpty(found) && File.Exists(found))
                    {
                        _cachedPath = found;
                        Debug.WriteLine($"IpatoolRunner: recursive found: {_cachedPath}");
                        return _cachedPath;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"IpatoolRunner: recursive search failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IpatoolRunner:GetIpatoolPath error: {ex.Message}");
            }

            _cachedPath = "ipatool.exe";
            Debug.WriteLine("IpatoolRunner: fallback to 'ipatool.exe'");
            return _cachedPath;
        }

        public static async Task<string> RunIpatoolAsync(string arguments, int timeoutMs = 15000)
        {
            var exePath = GetIpatoolPath();
            var workDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;

            // Diagnostic exec log
            try
            {
                var diagDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPAbuyer", "diag");
                Directory.CreateDirectory(diagDir);
                File.AppendAllText(Path.Combine(diagDir, "exec.log"), $"[{DateTime.Now}] Exec: {exePath} {arguments} WorkDir={workDir}\n");
            }
            catch { }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    WorkingDirectory = workDir
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                        return "无法启动进程";

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    var waitTask = process.WaitForExitAsync();
                    var finishedTask = await Task.WhenAny(waitTask, Task.Delay(timeoutMs));
                    if (finishedTask != waitTask)
                    {
                        try { process.Kill(true); } catch { }
                        var timeoutMsg = $"ipatool 调用超时（{timeoutMs}ms）: {arguments}";
                        try
                        {
                            var diagDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPAbuyer", "diag");
                            Directory.CreateDirectory(diagDir);
                            File.AppendAllText(Path.Combine(diagDir, "exec.log"), $"[{DateTime.Now}] TIMEOUT: {arguments}\n");
                        }
                        catch { }
                        return timeoutMsg;
                    }

                    string output = await outputTask;
                    string error = await errorTask;

                    // If caller requested JSON but output is not JSON, persist raw output for debugging
                    if (!string.IsNullOrWhiteSpace(output) && !(output.TrimStart().StartsWith("{") || output.TrimStart().StartsWith("[")))
                    {
                        try
                        {
                            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPAbuyer", "logs");
                            Directory.CreateDirectory(logDir);
                            File.AppendAllText(Path.Combine(logDir, "ipatool_debug.log"), $"[{DateTime.Now}] ARGS={arguments}\n{output}\n----\n");
                        }
                        catch { }
                    }

                    return string.IsNullOrWhiteSpace(error) ? output : error;
                }
            }
            catch (Exception ex)
            {
                return $"命令执行失败: {ex.Message}";
            }
        }

        // 高层封装：使用 KeychainConfig 自动注入 --keychain-passphrase，并执行 auth login
        public static Task<string> AuthLoginAsync(string email, string password, string authCode = "000000", bool nonInteractive = true)
        {
            var pass = KeychainConfig.GetPassphrase();
            var ni = nonInteractive ? "--non-interactive" : "";
            var arguments = $"auth login --email {EscapeArg(email)} --password \"{EscapeArg(password)}\" --keychain-passphrase {pass} {ni} --auth-code {authCode} --format json --verbose";
            return RunIpatoolAsync(arguments);
        }

        // 高层封装：带验证码的登录
        public static Task<string> AuthLoginWithCodeAsync(string email, string password, string authCode)
        {
            return AuthLoginAsync(email, password, authCode, true);
        }

        // 高层封装：购买 bundle identifier
        public static Task<string> PurchaseAsync(string bundleId, bool nonInteractive = true)
        {
            var pass = KeychainConfig.GetPassphrase();
            var ni = nonInteractive ? "--non-interactive" : "";
            var arguments = $"purchase --keychain-passphrase {pass} {ni} --bundle-identifier {EscapeArg(bundleId)}";
            return RunIpatoolAsync(arguments);
        }

        // 高层封装：搜索 appName，默认返回 JSON
        public static Task<string> SearchAsync(string appName, int limit = 5, bool formatJson = true, bool nonInteractive = true)
        {
            var pass = KeychainConfig.GetPassphrase();
            var fmt = formatJson ? "--format json" : "";
            var ni = nonInteractive ? "--non-interactive" : "";
            var arguments = $"search --keychain-passphrase {pass} {EscapeArg(appName)} --limit {limit} {ni} {fmt}";
            return RunIpatoolAsync(arguments);
        }

        private static string EscapeArg(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace("\"", "\\\"");
        }
    }
}
