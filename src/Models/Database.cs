using System.Diagnostics;
using System.IO;
using Windows.Storage;

namespace IPAbuyer.Models
{
    public class Database
    {

        public string? path; // 文件根目录
        public string? AppDBname = "PurchasedAppDb.db"; // 已购买app数据库文件名
        public string? AccountDBname = "KeychainConfig.db"; // 账户数据库文件名
        public string? AppDB; // 已购买app数据库路径
        public string? AccountDB; // 账户数据库路径

        public Database()
        {
            // 设置数据库路径

#if RELEASE // 生产版本
            try
            {
                // 获取安装目录
                path = ApplicationData.Current.LocalFolder.Path;
                AppDB = Path.Combine(path, AppDBname);
                AccountDB = Path.Combine(path, AccountDBname);
                
                // 如果数据库文件不存在则创建
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
            }
#elif DEBUG // 调试版本
            try
            {
                // 使用appdata文件夹
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPAbuyer");

                // 创建appdata内的目录
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                AppDB = Path.Combine(path, AppDBname);
                AccountDB = Path.Combine(path, AccountDBname);

                // 如果数据库文件不存在则创建
                if (!File.Exists(AppDB))
                {
                    File.Create(AppDB).Close();
                }
                if (!File.Exists(AccountDB))
                {
                    File.Create(AccountDB).Close();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"数据库错误: {ex.Message}");
            }
#endif
        }
    }
}
