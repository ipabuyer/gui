using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Windows.ApplicationModel.Resources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Windows.Security.Credentials;
using Windows.Storage;

namespace IPAbuyer.Common
{
    /// <summary>
    /// 本地配置读写（文件版，不使用 KeychainConfig.db）
    /// </summary>
    public static partial class KeychainConfig
    {
        private static readonly ResourceLoader Loader = new();
        private const string SettingsFileName = "settings.json";
        private const string PassphraseFileName = "passphrase.txt";
        private const string PassphraseVaultResource = "IPAbuyer.ipatool.passphrase";
        private const string DefaultPassphraseVaultUser = "__default__";
        private const string DefaultCountryCode = "cn";
        private const string DefaultPassphrase = "12345678";
        private static readonly object SyncRoot = new();

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

        public static void InitializeDatabase()
        {
            lock (SyncRoot)
            {
                EnsureStorageReady();
                RemoveLegacyKeychainDatabase();
                _ = LoadSettingsInternal();
            }
        }

        public static string? GetSecretKey(string username)
        {
            // 兼容旧调用：已移除数据库，不再存储 SecretKey。
            return null;
        }

        public static string GetCountryCode(string? account = null)
        {
            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                string candidate = string.IsNullOrWhiteSpace(settings.CountryCode)
                    ? DefaultCountryCode
                    : settings.CountryCode.Trim().ToLowerInvariant();
                return IsValidCountryCode(candidate) ? candidate : DefaultCountryCode;
            }
        }

        public static void SaveCountryCode(string countryCode, string? account = null)
        {
            if (string.IsNullOrWhiteSpace(countryCode))
            {
                throw new ArgumentException(L("KeychainConfig/Error/CountryCodeRequired"), nameof(countryCode));
            }

            string normalized = countryCode.Trim().ToLowerInvariant();
            if (!IsValidCountryCode(normalized))
            {
                throw new ArgumentException(L("KeychainConfig/Error/CountryCodeInvalid"), nameof(countryCode));
            }

            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.CountryCode = normalized;
                SaveSettingsInternal(settings);
            }
        }

        public static string GetDownloadDirectory()
        {
            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                string directory = string.IsNullOrWhiteSpace(settings.DownloadDirectory)
                    ? GetDefaultDownloadDirectory()
                    : Path.GetFullPath(settings.DownloadDirectory.Trim());
                Directory.CreateDirectory(directory);

                if (!string.Equals(settings.DownloadDirectory, directory, StringComparison.Ordinal))
                {
                    settings.DownloadDirectory = directory;
                    SaveSettingsInternal(settings);
                }

                return directory;
            }
        }

        public static void SavePassphrase(string account, string passphrase)
        {
            if (string.IsNullOrWhiteSpace(passphrase))
            {
                throw new ArgumentException(L("KeychainConfig/Error/PassphraseRequired"), nameof(passphrase));
            }

            lock (SyncRoot)
            {
                string normalizedPassphrase = passphrase.Trim();
                string vaultUser = NormalizePassphraseVaultUser(account);
                if (!TrySavePassphraseToVault(vaultUser, normalizedPassphrase))
                {
                    string path = GetPassphraseFilePath();
                    File.WriteAllText(path, normalizedPassphrase, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    return;
                }

                DeleteLegacyPassphraseFile();
            }
        }

        public static string? GetPassphrase(string? account)
        {
            lock (SyncRoot)
            {
                string vaultUser = NormalizePassphraseVaultUser(account);
                if (TryGetPassphraseFromVault(vaultUser, out string? vaultPassphrase))
                {
                    return vaultPassphrase;
                }

                if (TryMigrateLegacyPassphrase(vaultUser, out string? migratedPassphrase))
                {
                    return migratedPassphrase;
                }

                return DefaultPassphrase;
            }
        }

        public static string GetDefaultPassphrase()
        {
            return DefaultPassphrase;
        }

        public static void SaveDownloadDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException(L("KeychainConfig/Error/DownloadDirectoryRequired"), nameof(directoryPath));
            }

            string normalized = Path.GetFullPath(directoryPath.Trim());
            Directory.CreateDirectory(normalized);

            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.DownloadDirectory = normalized;
                SaveSettingsInternal(settings);
            }
        }

        public static bool GetDetailedIpatoolLogEnabled()
        {
            lock (SyncRoot)
            {
                return LoadSettingsInternal().DetailedIpatoolLogEnabled;
            }
        }

        public static void SaveDetailedIpatoolLogEnabled(bool enabled)
        {
            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.DetailedIpatoolLogEnabled = enabled;
                SaveSettingsInternal(settings);
            }
        }

        public static bool GetOwnedCheckEnabled()
        {
            lock (SyncRoot)
            {
                return LoadSettingsInternal().OwnedCheckEnabled;
            }
        }

        public static void SaveOwnedCheckEnabled(bool enabled)
        {
            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.OwnedCheckEnabled = enabled;
                SaveSettingsInternal(settings);
            }
        }

        public static bool IsValidCountryCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            return ValidCountryCodes.Contains(code.Trim().ToLowerInvariant());
        }

        public static bool IsMockAccount(string? username, string? password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            return string.Equals(username.Trim(), "test", StringComparison.OrdinalIgnoreCase)
                && string.Equals(password.Trim(), "test", StringComparison.Ordinal);
        }

        public static string GetDefaultDownloadDirectory()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloadPath = Path.Combine(userProfile, "Downloads");
            Directory.CreateDirectory(downloadPath);
            return downloadPath;
        }

        private static string GetSettingsFilePath()
        {
            string dataDirectory = ResolveDataDirectory();
            return Path.Combine(dataDirectory, SettingsFileName);
        }

        private static string GetPassphraseFilePath()
        {
            string dataDirectory = ResolveDataDirectory();
            return Path.Combine(dataDirectory, PassphraseFileName);
        }

        private static string NormalizePassphraseVaultUser(string? account)
        {
            return string.IsNullOrWhiteSpace(account)
                ? DefaultPassphraseVaultUser
                : account.Trim().ToLowerInvariant();
        }

        private static bool TrySavePassphraseToVault(string vaultUser, string passphrase)
        {
            try
            {
                var vault = new PasswordVault();
                TryRemovePassphraseFromVault(vault, vaultUser);
                vault.Add(new PasswordCredential(PassphraseVaultResource, vaultUser, passphrase));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetPassphraseFromVault(string vaultUser, out string? passphrase)
        {
            passphrase = null;
            try
            {
                var vault = new PasswordVault();
                PasswordCredential credential = vault.Retrieve(PassphraseVaultResource, vaultUser);
                credential.RetrievePassword();
                if (string.IsNullOrWhiteSpace(credential.Password))
                {
                    return false;
                }

                passphrase = credential.Password.Trim();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryMigrateLegacyPassphrase(string vaultUser, out string? passphrase)
        {
            passphrase = null;
            string path = GetPassphraseFilePath();
            if (!File.Exists(path))
            {
                return false;
            }

            string content;
            try
            {
                content = File.ReadAllText(path, Encoding.UTF8).Trim();
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            passphrase = content;
            if (TrySavePassphraseToVault(vaultUser, content))
            {
                DeleteLegacyPassphraseFile();
            }

            return true;
        }

        private static void TryRemovePassphraseFromVault(PasswordVault vault, string vaultUser)
        {
            try
            {
                PasswordCredential credential = vault.Retrieve(PassphraseVaultResource, vaultUser);
                vault.Remove(credential);
            }
            catch
            {
                // ignore missing or inaccessible vault entries
            }
        }

        private static void DeleteLegacyPassphraseFile()
        {
            try
            {
                string path = GetPassphraseFilePath();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignore cleanup errors; Vault remains the source of truth
            }
        }

        private static void EnsureStorageReady()
        {
            Directory.CreateDirectory(ResolveDataDirectory());
        }

        private static LocalSettingsModel LoadSettingsInternal()
        {
            string path = GetSettingsFilePath();
            if (!File.Exists(path))
            {
                var defaults = CreateDefaultSettings();
                SaveSettingsInternal(defaults);
                return defaults;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var model = CreateDefaultSettings();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    JObject root = JObject.Parse(json);
                    if (TryReadStringProperty(root, out string? countryValue, "country", "CountryCode"))
                    {
                        model.CountryCode = countryValue ?? DefaultCountryCode;
                    }

                    if (TryReadStringProperty(root, out string? downloadDirectoryValue, "download_dir", "DownloadDirectory"))
                    {
                        model.DownloadDirectory = downloadDirectoryValue ?? string.Empty;
                    }

                    if (TryReadBooleanProperty(root, out bool verboseValue, "verbose", "DetailedIpatoolLogEnabled"))
                    {
                        model.DetailedIpatoolLogEnabled = verboseValue;
                    }

                    if (TryReadBooleanProperty(root, out bool ownedCheckValue, "owned_check", "OwnedCheckEnabled"))
                    {
                        model.OwnedCheckEnabled = ownedCheckValue;
                    }
                }

                NormalizeSettings(model);
                SaveSettingsInternal(model);
                return model;
            }
            catch
            {
                var defaults = CreateDefaultSettings();
                SaveSettingsInternal(defaults);
                return defaults;
            }
        }

        private static void SaveSettingsInternal(LocalSettingsModel settings)
        {
            NormalizeSettings(settings);
            string path = GetSettingsFilePath();
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static void NormalizeSettings(LocalSettingsModel settings)
        {
            settings.CountryCode = string.IsNullOrWhiteSpace(settings.CountryCode)
                ? DefaultCountryCode
                : settings.CountryCode.Trim().ToLowerInvariant();
            if (!IsValidCountryCode(settings.CountryCode))
            {
                settings.CountryCode = DefaultCountryCode;
            }

            settings.DownloadDirectory = string.IsNullOrWhiteSpace(settings.DownloadDirectory)
                ? GetDefaultDownloadDirectory()
                : Path.GetFullPath(settings.DownloadDirectory.Trim());
        }

        private static LocalSettingsModel CreateDefaultSettings()
        {
            return new LocalSettingsModel
            {
                CountryCode = DefaultCountryCode,
                DownloadDirectory = GetDefaultDownloadDirectory(),
                DetailedIpatoolLogEnabled = false,
                OwnedCheckEnabled = false
            };
        }

        private static bool TryReadStringProperty(JObject root, out string? value, params string[] names)
        {
            foreach (string name in names)
            {
                if (root.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken? token)
                    && token.Type != JTokenType.Null)
                {
                    value = token.Type == JTokenType.String
                        ? token.Value<string>()
                        : token.ToString(Formatting.None);
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static bool TryReadBooleanProperty(JObject root, out bool value, params string[] names)
        {
            foreach (string name in names)
            {
                if (!root.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken? token)
                    || token.Type == JTokenType.Null)
                {
                    continue;
                }

                if (token.Type == JTokenType.Boolean)
                {
                    value = token.Value<bool>();
                    return true;
                }

                if (bool.TryParse(token.ToString(), out bool parsed))
                {
                    value = parsed;
                    return true;
                }
            }

            value = false;
            return false;
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

            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IPAbuyer");
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        private static void RemoveLegacyKeychainDatabase()
        {
            try
            {
                string legacyPath = Path.Combine(ResolveDataDirectory(), "KeychainConfig.db");
                if (File.Exists(legacyPath))
                {
                    File.Delete(legacyPath);
                }
            }
            catch
            {
                // ignore legacy cleanup errors
            }
        }

        private sealed class LocalSettingsModel
        {
            [JsonProperty("country")]
            public string CountryCode { get; set; } = DefaultCountryCode;

            [JsonProperty("download_dir")]
            public string DownloadDirectory { get; set; } = string.Empty;

            [JsonProperty("verbose")]
            public bool DetailedIpatoolLogEnabled { get; set; }

            [JsonProperty("owned_check")]
            public bool OwnedCheckEnabled { get; set; }
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }
    }
}
