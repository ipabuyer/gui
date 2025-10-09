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
                // 合并输出和错误流
                string result = output + error;
                return result;
            }
        }
    }
}
