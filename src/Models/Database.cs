using Windows.Storage;
using System.IO;

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
            catch
            {
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
