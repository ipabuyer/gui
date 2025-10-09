using Microsoft.Data.Sqlite;
using System;
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
        private static string _dbDirectory = string.Empty;
        private static string _dbPath = string.Empty;
        private static string _connectionString = string.Empty;

        /// <summary>
        /// 初始化数据库
        /// </summary>
        public static void InitializeDatabase()
        {
            _dbDirectory = Path.Combine(ResolveDataDirectory(), "db");
            _dbPath = Path.Combine(_dbDirectory, "KeychainConfig.db");
            _connectionString = $"Data Source={_dbPath}";
            if (!Directory.Exists(_dbDirectory))
            {
                Directory.CreateDirectory(_dbDirectory);
            }

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
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var selectCmd = connection.CreateCommand();
                selectCmd.CommandText = "SELECT Value FROM Settings WHERE Key = 'LastLoginUsername'";

                var result = selectCmd.ExecuteScalar();
                return result?.ToString();
            }
            catch (Exception ex)
            {
                throw new Exception($"获取最后登录用户失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 保存最后登录用户名（内部方法）
        /// </summary>
        private static void SaveLastLoginUsername(string username)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO Settings (Key, Value) 
                VALUES ('LastLoginUsername', @username)
                ON CONFLICT(Key) 
                DO UPDATE SET Value = @username";

            insertCmd.Parameters.AddWithValue("@username", username);
            insertCmd.ExecuteNonQuery();
        }

        private static string ResolveDataDirectory()
        {
            try
            {
                if (ApplicationData.Current != null)
                {
                    string localPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "IPAbuyer");
                    Directory.CreateDirectory(localPath);
                    return localPath;
                }
            }
            catch
            {
                // ignore and fall back
            }

            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPAbuyer");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
