using System;
using System.Diagnostics;
using System.IO;
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
#if DEBUG
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPAbuyer");
#else
                path = ApplicationData.Current.LocalFolder.Path;
#endif

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
            }
        }
    }
}
