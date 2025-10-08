using IPAbuyer.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace IPAbuyer.Common
{
    public static class ipatoolExecution
    {
        public static string authLogin(string account, string password, string authcode, string Passphrase)
        {
            string arguments = $"auth login --email {account} --password {password} --auth-code {authcode} --keychain-passphrase {Passphrase} --format json --non-interactive --verbose";
            return ExecuteIpatool(arguments);
        }

        public static string authLogout(string Passphrase)
        {
            string arguments = $"auth revoke --keychain-passphrase {Passphrase} --format json --non-interactive --verbose";
            return ExecuteIpatool(arguments);
        }

        public static string searchApp(string name, int limit, string Passphrase)
        {
            string arguments = $"search {name} --limit {limit} --keychain-passphrase {Passphrase} --format json --non-interactive --verbose";
            return ExecuteIpatool(arguments);
        }

        public static string purchaseApp(string bundleID, string Passphrase)
        {
            string arguments = $"purchase --bundle-identifier {bundleID} --keychain-passphrase {Passphrase} --format json --non-interactive --verbose";
            return ExecuteIpatool(arguments);
        }

        public static string ExecuteIpatool(string arguments)
        {
            // 查找当前目录下的ipatool.exe
            string currentDirPath = Path.Combine(Package.Current.InstalledLocation.Path, "ipatool.exe");
            string ipatoolPath;

            if (!File.Exists(currentDirPath))
            {
                Debug.WriteLine($"查找ipatool.exe路径时出错");
                ipatoolPath = Path.Combine(Environment.CurrentDirectory, "ipatool.exe");
            }
            else
            {
                Debug.WriteLine($"找到当前目录下的ipatool.exe");
                ipatoolPath = currentDirPath;
            }

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ipatoolPath,
                Arguments = arguments,
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

                process.WaitForExitAsync();

                return string.IsNullOrWhiteSpace(error) ? output : error;
            }
        }
    }
}
