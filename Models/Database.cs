using System;
using System.Diagnostics;
using System.IO;
using Windows.ApplicationModel;
using Windows.Storage;

namespace IPAbuyer.Models
{
    public class Database
    {
        public string? path { get; }
        public string appDbName { get; } = "PurchasedAppDb.db";
        public string? appDb { get; }

        public Database()
        {
            try
            {
                path = ResolveDatabaseDirectory();

                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                appDb = Path.Combine(path, appDbName);

                if (!File.Exists(appDb))
                {
                    File.Create(appDb).Close();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"数据库错误: {ex.Message}");
                try
                {
                    string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPAbuyer");
                    Directory.CreateDirectory(fallback);
                    path = fallback;
                    appDb = Path.Combine(path, appDbName);
                    if (!File.Exists(appDb))
                    {
                        File.Create(appDb).Close();
                    }
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"数据库兜底路径初始化失败: {fallbackEx.Message}");
                }
            }
        }

        private static string ResolveDatabaseDirectory()
        {
            // 打包环境优先使用 LocalState；取不到时回退到 LocalAppData\IPAbuyer。
            try
            {
                if (IsPackaged())
                {
                    string localState = ApplicationData.Current.LocalFolder.Path;
                    if (!string.IsNullOrWhiteSpace(localState))
                    {
                        Directory.CreateDirectory(localState);
                        return localState;
                    }
                }
            }
            catch
            {
                // ignore and fallback
            }

            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPAbuyer");
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        private static bool IsPackaged()
        {
            try
            {
                _ = Package.Current.Id.FullName;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
