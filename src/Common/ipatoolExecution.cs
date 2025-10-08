using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace IPAbuyer.Common
{
    public class ipatoolExecution
    {
        private string ipatoolPath;
        private string Passphrase = KeychainConfig.GetPassphrase();

        public ipatoolExecution()
        {
            findIpatoolPath();
        }

        // 查找ipatool.exe的路径，优先使用Include文件夹中的
        private void findIpatoolPath()
        {
            // 获取当前应用程序的基础目录
            string baseDirectory = AppContext.BaseDirectory;

            string includePath = Path.Combine(baseDirectory, "Include", "ipatool.exe");
            if (File.Exists(includePath))
            {
                Debug.WriteLine($"找到Include文件夹中的ipatool.exe: {includePath}");
                ipatoolPath = includePath;
            }

            // 查找当前目录下的ipatool.exe
            string currentDirPath = Path.Combine(baseDirectory, "ipatool.exe");

            if (File.Exists(currentDirPath))
            {
                Debug.WriteLine($"找到当前目录下的ipatool.exe");
                ipatoolPath = currentDirPath;
            }
            else
            {
                Debug.WriteLine($"查找ipatool.exe路径时出错");
                ipatoolPath = "ipatool.exe";
            }
        }

        public Task<string> authLogin(string account, string password, string authcode)
        {
            string arguments = $"auth login --email {account} --password {password} --auth-code {authcode} --keychain-passphrase {Passphrase} --format json --non-interactive --verbose";
            return ExecuteIpatool(arguments);
        }

        public Task<string> authLogout()
        {
            string arguments = $"auth revoke --keychain-passphrase {Passphrase} --format json --non-interactive --verbose";
            return ExecuteIpatool(arguments);
        }

        public Task<string> searchApp(string name, int limit)
        {
            string arguments = $"search {name} --limit {limit} --keychain-passphrase {Passphrase} --format json --non-interactive --verbose";
            return ExecuteIpatool(arguments);
        }

        public Task<string> purchaseApp(string bundleID)
        {
            string arguments = $"purchase --bundle-identifier {bundleID} --keychain-passphrase {Passphrase} --format json --non-interactive --verbose";
            return ExecuteIpatool(arguments);
        }

        private async Task<string> ExecuteIpatool(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = $"cmd.exe",
                Arguments = $"/c \"{ipatoolPath} {arguments}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };

            using (var process = Process.Start(psi))
            {
                if (process == null)
                {
                    return $"无法启动进程";
                }

                var output = process.StandardOutput.ToString();
                var error = process.StandardError.ToString();

                await process.WaitForExitAsync();

                return string.IsNullOrWhiteSpace(error) ? output : error;
            }
        }
    }
}
