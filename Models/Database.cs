using Microsoft.Windows.ApplicationModel.Resources;
using System.Diagnostics;
using Windows.Storage;

namespace IPAbuyer.Models
{
    public class Database
    {
        private static readonly ResourceLoader Loader = new();
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
                Debug.WriteLine(LF("Database/Debug/Error", ex.Message));
            }
        }

        private static string ResolveDatabaseDirectory()
        {
            string localState = ApplicationData.Current.LocalFolder.Path;
            if (string.IsNullOrWhiteSpace(localState))
            {
                throw new InvalidOperationException(Loader.GetString("Database/Debug/LocalStatePathMissing"));
            }

            Directory.CreateDirectory(localState);
            return localState;
        }

        private static string LF(string key, params object[] args)
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, Loader.GetString(key), args);
        }
    }
}
