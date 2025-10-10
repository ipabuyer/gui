using System.Diagnostics;
using System.IO;
using Windows.Storage;

namespace IPAbuyer.Models
{
    public class Database
    {
        public string? packagepath;
        public string? appdatapath;
        public string? AppDBname = "PurchasedAppDb.db";
        public string? AccountDBname = "KeychainConfig.db";
        public string? AppDB;
        public string? AccountDB;

        public Database()
        {
            try
            {
                packagepath = ApplicationData.Current.LocalFolder.Path;
                AppDB = Path.Combine(packagepath, AppDBname);
                AccountDB = Path.Combine(packagepath, AccountDBname);
                if (!File.Exists(AppDB))
                {
                    File.Create(AppDB).Close();
                }
                if (!File.Exists(AccountDB))
                {
                    File.Create(AccountDB).Close();
                }
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"数据库错误: {ex.Message}");
                appdatapath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPAbuyer");
                if (!Directory.Exists(appdatapath))
                    Directory.CreateDirectory(appdatapath);
                AppDB = Path.Combine(appdatapath, AppDBname);
                AccountDB = Path.Combine(appdatapath, AccountDBname);
                if (!File.Exists(AppDB))
                {
                    File.Create(AppDB).Close();
                }
                if (!File.Exists(AccountDB))
                {
                    File.Create(AccountDB).Close();
                }
            }
        }
    }
}
