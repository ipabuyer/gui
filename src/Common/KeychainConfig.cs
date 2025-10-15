using IPAbuyer.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;

namespace IPAbuyer.Common
{
    /// <summary>
    /// SQLite 数据库交互帮助类
    /// </summary>
    public static class KeychainConfig
    {
        private static string _dbPath = string.Empty;
        private static string _connectionString = string.Empty;
        private const string LastLoginKey = "LastLoginUsername";
        private const string CountryCodeKey = "CountryCode";
        private const string DefaultCountryCode = "cn";
        private static readonly HashSet<string> ValidCountryCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ad","ae","af","ag","ai","al","am","ao","aq","ar","as","at","au","aw","ax","az",
            "ba","bb","bd","be","bf","bg","bh","bi","bj","bl","bm","bn","bo","bq","br","bs","bt","bv","bw","by","bz",
            "ca","cc","cd","cf","cg","ch","ci","ck","cl","cm","cn","co","cr","cu","cv","cw","cx","cy","cz",
            "de","dj","dk","dm","do",
            "dz","ec","ee","eg","eh","er","es","et",
            "fi","fj","fk","fm","fo","fr",
            "ga","gb","gd","ge","gf","gg","gh","gi","gl","gm","gn","gp","gq","gr","gs","gt","gu","gw","gy",
            "hk","hm","hn","hr","ht","hu",
            "id","ie","il","im","in","io","iq","ir","is","it",
            "je","jm","jo","jp",
            "ke","kg","kh","ki","km","kn","kp","kr","kw","ky","kz",
            "la","lb","lc","li","lk","lr","ls","lt","lu","lv","ly",
            "ma","mc","md","me","mf","mg","mh","mk","ml","mm","mn","mo","mp","mq","mr","ms","mt","mu","mv","mw","mx","my","mz",
            "na","nc","ne","nf","ng","ni","nl","no","np","nr","nu","nz",
            "om",
            "pa","pe","pf","pg","ph","pk","pl","pm","pn","pr","ps","pt","pw","py",
            "qa",
            "re","ro","rs","ru","rw",
            "sa","sb","sc","sd","se","sg","sh","si","sj","sk","sl","sm","sn","so","sr","ss","st","sv","sx","sy","sz",
            "tc","td","tf","tg","th","tj","tk","tl","tm","tn","to","tr","tt","tv","tw","tz",
            "ua","ug","um","us","uy","uz",
            "va","vc","ve","vg","vi","vn","vu",
            "wf","ws",
            "ye","yt",
            "za","zm","zw"
        };

        /// <summary>
        /// 初始化数据库
        /// </summary>
        public static void InitializeDatabase()
        {
            Database database = new Database();
            _dbPath = database.AccountDB ?? throw new InvalidOperationException("账户数据库路径未初始化");
            _connectionString = $"Data Source={_dbPath}";

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var createTableCmd = connection.CreateCommand();
            createTableCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE,
                    SecretKey TEXT NOT NULL
                );
                
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );";
            createTableCmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 生成10位随机字符串密钥（包含大小写字母和数字）
        /// </summary>
        private static string GenerateRandomKey()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            const int keyLength = 10;

            using var rng = RandomNumberGenerator.Create();
            var result = new char[keyLength];
            var buffer = new byte[keyLength];

            rng.GetBytes(buffer);

            for (int i = 0; i < keyLength; i++)
            {
                result[i] = chars[buffer[i] % chars.Length];
            }

            return new string(result);
        }

        /// <summary>
        /// 1. 输入用户名返回密钥
        /// </summary>
        /// <param name="username">用户名</param>
        /// <returns>密钥，如果用户不存在则返回 null</returns>
        public static string? GetSecretKey(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var selectCmd = connection.CreateCommand();
                selectCmd.CommandText = "SELECT SecretKey FROM Users WHERE Username = @username";
                selectCmd.Parameters.AddWithValue("@username", username);

                var result = selectCmd.ExecuteScalar();
                return result?.ToString(); // 添加 ? 操作符以安全处理 null
            }
            catch (Exception ex)
            {
                throw new Exception($"查询密钥失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 2. 输入用户名生成密钥，返回密钥，写入数据库
        /// </summary>
        /// <param name="username">用户名</param>
        /// <returns>生成的密钥</returns>
        public static string GenerateAndSaveSecretKey(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("用户名不能为空");

            try
            {

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                // 先尝试读取已有密钥，避免重复生成导致登录状态失效
                var selectCmd = connection.CreateCommand();
                selectCmd.CommandText = "SELECT SecretKey FROM Users WHERE Username = @username";
                selectCmd.Parameters.AddWithValue("@username", username);

                var existingKey = selectCmd.ExecuteScalar()?.ToString();
                if (!string.IsNullOrEmpty(existingKey))
                {
                    SaveLastLoginUsername(username);
                    return existingKey;
                }

                var secretKey = GenerateRandomKey();
                // 使用 INSERT OR REPLACE 来处理用户已存在的情况
                var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO Users (Username, SecretKey) 
                    VALUES (@username, @secretKey)
                    ON CONFLICT(Username) 
                    DO UPDATE SET SecretKey = @secretKey";

                insertCmd.Parameters.AddWithValue("@username", username);
                insertCmd.Parameters.AddWithValue("@secretKey", secretKey);
                insertCmd.ExecuteNonQuery();

                // 更新最后登录用户
                SaveLastLoginUsername(username);

                return secretKey;
            }
            catch (Exception ex)
            {
                throw new Exception($"生成并保存密钥失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 3. 查找最后一次登录用户名
        /// </summary>
        /// <returns>最后登录的用户名，如果不存在则返回 null</returns>
        public static string? GetLastLoginUsername()
        {
            try
            {
                if (string.IsNullOrEmpty(_connectionString))
                {
                    InitializeDatabase();
                }

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var selectCmd = connection.CreateCommand();
                selectCmd.CommandText = "SELECT Value FROM Settings WHERE Key = @key";
                selectCmd.Parameters.AddWithValue("@key", LastLoginKey);

                var result = selectCmd.ExecuteScalar();
                return result?.ToString();
            }
            catch (Exception ex)
            {
                throw new Exception($"获取最后登录用户失败: {ex.Message}", ex);
            }
        }

        public static void ClearLastLoginUsername()
        {
            try
            {
                if (string.IsNullOrEmpty(_connectionString))
                {
                    InitializeDatabase();
                }

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var deleteCmd = connection.CreateCommand();
                deleteCmd.CommandText = "DELETE FROM Settings WHERE Key = @key";
                deleteCmd.Parameters.AddWithValue("@key", LastLoginKey);
                deleteCmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception($"清除最后登录用户失败: {ex.Message}", ex);
            }
        }

        public static string GetCountryCode(string? account = null)
        {
            try
            {
                if (string.IsNullOrEmpty(_connectionString))
                {
                    InitializeDatabase();
                }

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                string primaryKey = BuildCountryCodeKey(account);
                string? primaryValue = ReadSettingValue(connection, primaryKey);

                if (TryNormalizeCountryCode(primaryValue, out string normalizedPrimary))
                {
                    return normalizedPrimary;
                }

                if (!string.IsNullOrWhiteSpace(account))
                {
                    string? defaultValue = ReadSettingValue(connection, CountryCodeKey);
                    if (TryNormalizeCountryCode(defaultValue, out string normalizedDefault))
                    {
                        return normalizedDefault;
                    }
                }

                return DefaultCountryCode;
            }
            catch (Exception ex)
            {
                throw new Exception($"获取国家/地区代码失败: {ex.Message}", ex);
            }
        }

        public static void SaveCountryCode(string countryCode, string? account = null)
        {
            if (string.IsNullOrWhiteSpace(countryCode))
            {
                throw new ArgumentException("国家/地区代码不能为空", nameof(countryCode));
            }

            string normalized = countryCode.Trim().ToLowerInvariant();

            if (!IsValidCountryCode(normalized))
            {
                throw new ArgumentException("国家/地区代码格式无效", nameof(countryCode));
            }

            try
            {
                if (string.IsNullOrEmpty(_connectionString))
                {
                    InitializeDatabase();
                }

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                INSERT INTO Settings (Key, Value)
                VALUES (@key, @value)
                ON CONFLICT(Key)
                DO UPDATE SET Value = @value";

                insertCmd.Parameters.AddWithValue("@key", BuildCountryCodeKey(account));
                insertCmd.Parameters.AddWithValue("@value", normalized);
                insertCmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception($"保存国家/地区代码失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 保存最后登录用户名（内部方法）
        /// </summary>
        private static void SaveLastLoginUsername(string username)
        {
            if (string.IsNullOrEmpty(_connectionString))
            {
                InitializeDatabase();
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO Settings (Key, Value) 
                VALUES (@key, @username)
                ON CONFLICT(Key) 
                DO UPDATE SET Value = @username";

            insertCmd.Parameters.AddWithValue("@key", LastLoginKey);
            insertCmd.Parameters.AddWithValue("@username", username);
            insertCmd.ExecuteNonQuery();
        }

        public static bool IsValidCountryCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            return ValidCountryCodes.Contains(code.Trim().ToLowerInvariant());
        }

        private static string? ReadSettingValue(SqliteConnection connection, string key)
        {
            using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT Value FROM Settings WHERE Key = @key";
            selectCmd.Parameters.AddWithValue("@key", key);
            var result = selectCmd.ExecuteScalar();
            return result?.ToString();
        }

        private static bool TryNormalizeCountryCode(string? value, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string candidate = value.Trim().ToLowerInvariant();
            if (!IsValidCountryCode(candidate))
            {
                return false;
            }

            normalized = candidate;
            return true;
        }

        private static string BuildCountryCodeKey(string? account)
        {
            string normalizedAccount = NormalizeAccountForKey(account);
            return string.IsNullOrEmpty(normalizedAccount)
                ? CountryCodeKey
                : $"{CountryCodeKey}:{normalizedAccount}";
        }

        private static string NormalizeAccountForKey(string? account)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                return string.Empty;
            }

            return account.Trim().ToLowerInvariant();
        }

        private static string ResolveDataDirectory()
        {
            try
            {
                if (ApplicationData.Current != null)
                {
                    string localPath = ApplicationData.Current.LocalFolder.Path;
                    Directory.CreateDirectory(localPath);
                    return localPath;
                }
            }
            catch
            {
                // ignore and fall back
            }

            string fallback = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
