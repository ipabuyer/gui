using IPAbuyer.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace IPAbuyer.Common
{
    public static class ipatoolExecution
    {
        public static string _account = "example@icloud.com";
        public static string _password = "examplePassword";
        public static string _authcode = "000000";
        public static string _Passphrase = "12345678";

        public static string authLogin(string account, string password, string authcode)
        {
            _account = account;
            _password = password;
            _authcode = authcode;
            string arguments = $"auth login --email {_account} --password {_password} --auth-code {_authcode}";
            return ExecuteIpatool(arguments);
        }

        public static string authLogout(string account)
        {
            _account = account;
            string arguments = $"auth revoke";
            return ExecuteIpatool(arguments);
        }

        public static string searchApp(string name, int limit, string account)
        {
            _account = account;
            string arguments = $"search {name} --limit {limit}";
            return ExecuteIpatool(arguments);
        }

        public static string purchaseApp(string bundleID, string account)
        {
            _account = account;
            string arguments = $"purchase --bundle-identifier {bundleID}";
            return ExecuteIpatool(arguments);
        }

        public static string ExecuteIpatool(string arguments)
        {
            // 查找当前目录下的ipatool.exe
            string currentDirPath = AppContext.BaseDirectory;
            string ipatoolPath = Path.Combine(currentDirPath, "ipatool.exe");

            if (File.Exists(ipatoolPath))
            {
                Debug.WriteLine($"找到当前目录下的ipatool.exe");
            }
            else
            {
                Debug.WriteLine($"查找ipatool.exe路径时出错");
                ipatoolPath = "ipatool.exe";
            }

            var existingKey = KeychainConfig.GetSecretKey(_account);
            if (!string.IsNullOrEmpty(existingKey))
            {
                _Passphrase = existingKey;
            }
            else
            {
                _Passphrase = KeychainConfig.GenerateAndSaveSecretKey(_account);
            }

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ipatoolPath,
                Arguments = $"{arguments} --keychain-passphrase {_Passphrase} --format json --non-interactive --verbose",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            psi.EnvironmentVariables["NO_COLOR"] = "1";
            psi.EnvironmentVariables["TERM"] = "dumb";

            using (var process = Process.Start(psi))
            {
                if (process == null)
                {
                    return "无法启动进程";
                }

                // 使用异步读取避免死锁
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                
                // 等待两个任务完成，避免使用 .Result 导致的死锁
                Task.WaitAll(outputTask, errorTask);
                
                string output = outputTask.Result;
                string error = errorTask.Result;
                
                process.WaitForExit();
                Debug.WriteLine($"ipatool output: {output}");
                Debug.WriteLine($"ipatool stderr: {error}");
                
                // 只返回标准输出,stderr通常包含日志信息
                // 如果输出为空,则返回错误信息
                if (string.IsNullOrWhiteSpace(output))
                {
                    return error;
                }
                
                // 尝试清理输出,只保留有效的JSON部分
                output = output.Trim();
                
                // 如果输出中有多个JSON对象(用换行分隔),需要过滤掉日志行
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                // 优先查找包含 "apps" 或 "success" 的有效JSON响应
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if ((trimmedLine.StartsWith("{") || trimmedLine.StartsWith("[")) 
                        && !trimmedLine.Contains("\"level\":\"debug\"")
                        && !trimmedLine.Contains("\"level\":\"info\"")
                        && !trimmedLine.Contains("\"level\":\"warning\""))
                    {
                        // 这看起来是一个有效的响应JSON,不是日志
                        if (trimmedLine.Contains("\"apps\"") 
                            || trimmedLine.Contains("\"success\"") 
                            || trimmedLine.Contains("\"error\"")
                            || trimmedLine.Contains("\"message\""))
                        {
                            return trimmedLine;
                        }
                    }
                }
                
                // 如果没有找到明确的响应,返回第一个非日志的JSON行
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if ((trimmedLine.StartsWith("{") || trimmedLine.StartsWith("["))
                        && !trimmedLine.Contains("\"level\":"))
                    {
                        return trimmedLine;
                    }
                }
                
                // 最后的备选:返回原始输出
                return output;
            }
        }
    }
}
