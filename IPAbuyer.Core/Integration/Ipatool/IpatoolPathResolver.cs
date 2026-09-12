using IPAbuyer.Core.Configuration;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IPAbuyer.Core.Integration.Ipatool
{
    internal static class IpatoolPathResolver
    {
        private static readonly ResourceLoader Loader = new();

        internal static string ResolveExecutablePath()
        {
            if (string.Equals(IpatoolSettings.GetFlavor(), IpatoolSettings.FlavorCustom, StringComparison.OrdinalIgnoreCase)
                && IpatoolSettings.HasUsableCustomPath())
            {
                return IpatoolSettings.GetCustomPath();
            }

            string baseDirectory = AppContext.BaseDirectory;
            string defaultPath = Path.Combine(baseDirectory, "ipatool.exe");
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }

            string architectureSuffix = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "amd64",
                _ => string.Empty
            };
            string includePath = Path.Combine(baseDirectory, "Include", $"ipatool-2.5.0-windows-{architectureSuffix}.exe");
            if (!string.IsNullOrWhiteSpace(architectureSuffix) && File.Exists(includePath))
            {
                return includePath;
            }

            Debug.WriteLine(Loader.GetString("Ipatool/Debug/FallbackToPath"));
            return "ipatool.exe";
        }

        internal static string GetWorkingDirectory(string executablePath)
        {
            return Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
        }

        internal static void DeleteCookieLockFile()
        {
            try
            {
                string lockPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ipatool", "cookies.lock");
                if (File.Exists(lockPath))
                {
                    File.Delete(lockPath);
                    Debug.WriteLine(string.Format(System.Globalization.CultureInfo.CurrentCulture, Loader.GetString("Ipatool/Debug/DeleteCookieLock"), lockPath));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format(System.Globalization.CultureInfo.CurrentCulture, Loader.GetString("Ipatool/Debug/DeleteCookieLockFailed"), ex.Message));
            }
        }
    }
}
