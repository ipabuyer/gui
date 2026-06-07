using Microsoft.Windows.ApplicationModel.Resources;
using System.Text;
using System.Text.Json;
using Windows.Security.Credentials;
using Windows.Storage;

namespace IPAbuyer.Common
{
    /// <summary>
    /// 本地配置读写（LocalSettings 版，不使用 KeychainConfig.db）
    /// </summary>
    public static partial class KeychainConfig
    {
        private static readonly ResourceLoader Loader = new();
        private const string SettingsFileName = "settings.json";
        private const string SettingsMigratedSuffix = ".migrated";
        private const string PassphraseFileName = "passphrase.txt";
        private const string PassphraseVaultResource = "IPAbuyer.ipatool.passphrase";
        private const string DefaultPassphraseVaultUser = "__default__";
        private const string DefaultCountryCode = "cn";
        private const string CountryCodeSettingKey = "CountryCode";
        private const string DownloadDirectorySettingKey = "DownloadDirectory";
        private const string DetailedIpatoolLogEnabledSettingKey = "DetailedIpatoolLogEnabled";
        private const string OwnedCheckEnabledSettingKey = "OwnedCheckEnabled";
        private const string KeychainPassphraseRotationEnabledSettingKey = "KeychainPassphraseRotationEnabled";
        public const string IpatoolFlavorMain = "Main";
        public const string IpatoolFlavorLegacy = "Legacy";
        public const string IpatoolFlavorCustom = "Custom";
        private const string IpatoolFlavorSettingKey = "IpatoolFlavor";
        private const string CustomIpatoolPathSettingKey = "CustomIpatoolPath";
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

        public static void SavePassphrase(string passphrase)
        {
            if (string.IsNullOrWhiteSpace(passphrase))
            {
                throw new ArgumentException(L("KeychainConfig/Error/PassphraseRequired"), nameof(passphrase));
            }

            lock (SyncRoot)
            {
                string normalizedPassphrase = passphrase.Trim();
                if (!TrySavePassphraseToVault(normalizedPassphrase))
                {
                    SaveLegacyPassphrase(normalizedPassphrase);
                    return;
                }

                DeleteLegacyPassphraseFile();
            }
        }

        public static string? GetPassphrase(string? account)
        {
            lock (SyncRoot)
            {
                if (TryGetPassphraseFromVault(out string? vaultPassphrase))
                {
                    return vaultPassphrase;
                }

                if (TryMigrateLegacyPassphrase(out string? migratedPassphrase))
                {
                    return migratedPassphrase;
                }

                string generatedPassphrase = CreateDefaultPassphrase();
                if (!TrySavePassphraseToVault(generatedPassphrase))
                {
                    SaveLegacyPassphrase(generatedPassphrase);
                }

                return generatedPassphrase;
            }
        }

        public static string GetDefaultPassphrase()
        {
            return GetPassphrase(null) ?? CreateDefaultPassphrase();
        }

        public static string RotateDefaultPassphrase()
        {
            lock (SyncRoot)
            {
                string passphrase = CreateDefaultPassphrase();
                if (!TrySavePassphraseToVault(passphrase))
                {
                    SaveLegacyPassphrase(passphrase);
                }
                else
                {
                    DeleteLegacyPassphraseFile();
                }

                return passphrase;
            }
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

        public static bool GetKeychainPassphraseRotationEnabled()
        {
            lock (SyncRoot)
            {
                return LoadSettingsInternal().KeychainPassphraseRotationEnabled;
            }
        }

        public static void SaveKeychainPassphraseRotationEnabled(bool enabled)
        {
            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.KeychainPassphraseRotationEnabled = enabled;
                SaveSettingsInternal(settings);
            }
        }

        public static string GetIpatoolFlavor()
        {
            lock (SyncRoot)
            {
                return LoadSettingsInternal().IpatoolFlavor;
            }
        }

        public static void SaveIpatoolFlavor(string flavor)
        {
            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.IpatoolFlavor = NormalizeIpatoolFlavor(flavor);
                SaveSettingsInternal(settings);
            }
        }

        public static string GetCustomIpatoolPath()
        {
            lock (SyncRoot)
            {
                return LoadSettingsInternal().CustomIpatoolPath;
            }
        }

        public static bool HasUsableCustomIpatoolPath()
        {
            lock (SyncRoot)
            {
                string path = LoadSettingsInternal().CustomIpatoolPath;
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            }
        }

        public static void SaveCustomIpatoolPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(L("KeychainConfig/Error/CustomIpatoolPathRequired"), nameof(path));
            }

            string normalized = Path.GetFullPath(path.Trim());
            if (!File.Exists(normalized))
            {
                throw new FileNotFoundException(L("KeychainConfig/Error/CustomIpatoolPathNotFound"), normalized);
            }

            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.CustomIpatoolPath = normalized;
                SaveSettingsInternal(settings);
            }
        }

        public static void DeleteCustomIpatoolPath()
        {
            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.CustomIpatoolPath = string.Empty;
                if (string.Equals(settings.IpatoolFlavor, IpatoolFlavorCustom, StringComparison.OrdinalIgnoreCase))
                {
                    settings.IpatoolFlavor = IpatoolFlavorMain;
                }

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

        private static string CreateDefaultPassphrase()
        {
            return Guid.NewGuid().ToString("N");
        }

        private static bool TrySavePassphraseToVault(string passphrase)
        {
            try
            {
                var vault = new PasswordVault();
                RemoveAllPassphraseEntriesFromVault(vault);
                vault.Add(new PasswordCredential(PassphraseVaultResource, DefaultPassphraseVaultUser, passphrase));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetPassphraseFromVault(out string? passphrase)
        {
            passphrase = null;
            try
            {
                var vault = new PasswordVault();
                PasswordCredential credential = vault.Retrieve(PassphraseVaultResource, DefaultPassphraseVaultUser);
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

        private static bool TryMigrateLegacyPassphrase(out string? passphrase)
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
            if (TrySavePassphraseToVault(content))
            {
                DeleteLegacyPassphraseFile();
            }

            return true;
        }

        private static void SaveLegacyPassphrase(string passphrase)
        {
            string path = GetPassphraseFilePath();
            File.WriteAllText(path, passphrase, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static void RemoveAllPassphraseEntriesFromVault(PasswordVault vault)
        {
            try
            {
                foreach (PasswordCredential credential in vault.FindAllByResource(PassphraseVaultResource))
                {
                    vault.Remove(credential);
                }
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
            TryMigrateLegacySettingsFile();

            var model = CreateDefaultSettings();
            var values = ApplicationData.Current.LocalSettings.Values;
            if (TryReadStringSetting(values, out string? countryValue, CountryCodeSettingKey, "country"))
            {
                model.CountryCode = countryValue ?? DefaultCountryCode;
            }

            if (TryReadStringSetting(values, out string? downloadDirectoryValue, DownloadDirectorySettingKey, "download_dir"))
            {
                model.DownloadDirectory = downloadDirectoryValue ?? string.Empty;
            }

            if (TryReadBooleanSetting(values, out bool verboseValue, DetailedIpatoolLogEnabledSettingKey, "verbose"))
            {
                model.DetailedIpatoolLogEnabled = verboseValue;
            }

            if (TryReadBooleanSetting(values, out bool ownedCheckValue, OwnedCheckEnabledSettingKey, "owned_check"))
            {
                model.OwnedCheckEnabled = ownedCheckValue;
            }

            if (TryReadBooleanSetting(values, out bool rotationValue, KeychainPassphraseRotationEnabledSettingKey, "keychain_passphrase_rotation"))
            {
                model.KeychainPassphraseRotationEnabled = rotationValue;
            }

            if (TryReadStringSetting(values, out string? ipatoolFlavorValue, IpatoolFlavorSettingKey, "AuthIpatoolFlavor", "auth_ipatool_flavor"))
            {
                model.IpatoolFlavor = NormalizeIpatoolFlavor(ipatoolFlavorValue);
            }

            if (TryReadStringSetting(values, out string? customIpatoolPathValue, CustomIpatoolPathSettingKey, "custom_ipatool_path"))
            {
                model.CustomIpatoolPath = customIpatoolPathValue ?? string.Empty;
            }

            NormalizeSettings(model);
            SaveSettingsInternal(model);
            return model;
        }

        private static void TryMigrateLegacySettingsFile()
        {
            string path = GetSettingsFilePath();
            if (!File.Exists(path))
            {
                return;
            }

            LocalSettingsModel model;
            try
            {
                model = ReadLegacySettingsFile(path);
                NormalizeSettings(model);
            }
            catch
            {
                return;
            }

            SaveSettingsInternal(model);
            MarkLegacySettingsFileMigrated(path);
        }

        private static void SaveSettingsInternal(LocalSettingsModel settings)
        {
            NormalizeSettings(settings);
            var values = ApplicationData.Current.LocalSettings.Values;
            values[CountryCodeSettingKey] = settings.CountryCode;
            values[DownloadDirectorySettingKey] = settings.DownloadDirectory;
            values[DetailedIpatoolLogEnabledSettingKey] = settings.DetailedIpatoolLogEnabled;
            values[OwnedCheckEnabledSettingKey] = settings.OwnedCheckEnabled;
            values[KeychainPassphraseRotationEnabledSettingKey] = settings.KeychainPassphraseRotationEnabled;
            values[IpatoolFlavorSettingKey] = settings.IpatoolFlavor;
            values[CustomIpatoolPathSettingKey] = settings.CustomIpatoolPath;
            RemoveLegacySettingAliases(values);
        }

        private static void RemoveLegacySettingAliases(IDictionary<string, object> values)
        {
            values.Remove("country");
            values.Remove("download_dir");
            values.Remove("verbose");
            values.Remove("owned_check");
            values.Remove("keychain_passphrase_rotation");
            values.Remove("AuthIpatoolFlavor");
            values.Remove("auth_ipatool_flavor");
            values.Remove("custom_ipatool_path");
        }

        private static LocalSettingsModel ReadLegacySettingsFile(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            var model = CreateDefaultSettings();
            if (string.IsNullOrWhiteSpace(json))
            {
                return model;
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
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

            if (TryReadBooleanProperty(root, out bool rotationValue, "keychain_passphrase_rotation", "KeychainPassphraseRotationEnabled"))
            {
                model.KeychainPassphraseRotationEnabled = rotationValue;
            }

            if (TryReadStringProperty(root, out string? ipatoolFlavorValue, "IpatoolFlavor", "AuthIpatoolFlavor", "auth_ipatool_flavor"))
            {
                model.IpatoolFlavor = NormalizeIpatoolFlavor(ipatoolFlavorValue);
            }

            if (TryReadStringProperty(root, out string? customIpatoolPathValue, "CustomIpatoolPath", "custom_ipatool_path"))
            {
                model.CustomIpatoolPath = customIpatoolPathValue ?? string.Empty;
            }

            return model;
        }

        private static void MarkLegacySettingsFileMigrated(string path)
        {
            try
            {
                File.Move(path, path + SettingsMigratedSuffix, overwrite: true);
            }
            catch
            {
                // LocalSettings already contains the migrated values; keep the original file if it cannot be renamed.
            }
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

            settings.IpatoolFlavor = NormalizeIpatoolFlavor(settings.IpatoolFlavor);
            settings.CustomIpatoolPath = string.IsNullOrWhiteSpace(settings.CustomIpatoolPath)
                ? string.Empty
                : Path.GetFullPath(settings.CustomIpatoolPath.Trim());
            if (string.Equals(settings.IpatoolFlavor, IpatoolFlavorCustom, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(settings.CustomIpatoolPath) || !File.Exists(settings.CustomIpatoolPath)))
            {
                settings.IpatoolFlavor = IpatoolFlavorMain;
            }
        }

        private static LocalSettingsModel CreateDefaultSettings()
        {
            return new LocalSettingsModel
            {
                CountryCode = DefaultCountryCode,
                DownloadDirectory = GetDefaultDownloadDirectory(),
                DetailedIpatoolLogEnabled = false,
                OwnedCheckEnabled = false,
                KeychainPassphraseRotationEnabled = true,
                IpatoolFlavor = IpatoolFlavorMain,
                CustomIpatoolPath = string.Empty
            };
        }

        private static string NormalizeIpatoolFlavor(string? flavor)
        {
            if (string.Equals(flavor?.Trim(), IpatoolFlavorCustom, StringComparison.OrdinalIgnoreCase))
            {
                return IpatoolFlavorCustom;
            }

            if (string.Equals(flavor?.Trim(), IpatoolFlavorLegacy, StringComparison.OrdinalIgnoreCase))
            {
                return IpatoolFlavorLegacy;
            }

            return IpatoolFlavorMain;
        }

        private static bool TryReadStringProperty(JsonElement root, out string? value, params string[] names)
        {
            foreach (string name in names)
            {
                if (JsonPayload.TryGetProperty(root, name, out JsonElement token)
                    && token.ValueKind != JsonValueKind.Null
                    && token.ValueKind != JsonValueKind.Undefined)
                {
                    value = token.ValueKind == JsonValueKind.String
                        ? token.GetString()
                        : token.GetRawText();
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static bool TryReadBooleanProperty(JsonElement root, out bool value, params string[] names)
        {
            foreach (string name in names)
            {
                if (!JsonPayload.TryGetProperty(root, name, out JsonElement token)
                    || token.ValueKind == JsonValueKind.Null
                    || token.ValueKind == JsonValueKind.Undefined)
                {
                    continue;
                }

                if (token.ValueKind == JsonValueKind.True || token.ValueKind == JsonValueKind.False)
                {
                    value = token.GetBoolean();
                    return true;
                }

                string? text = token.ValueKind == JsonValueKind.String
                    ? token.GetString()
                    : token.GetRawText();
                if (bool.TryParse(text, out bool parsed))
                {
                    value = parsed;
                    return true;
                }
            }

            value = false;
            return false;
        }

        private static bool TryReadStringSetting(IDictionary<string, object> values, out string? value, params string[] names)
        {
            foreach (string name in names)
            {
                if (values.TryGetValue(name, out object? raw) && raw != null)
                {
                    value = raw as string ?? raw.ToString();
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static bool TryReadBooleanSetting(IDictionary<string, object> values, out bool value, params string[] names)
        {
            foreach (string name in names)
            {
                if (!values.TryGetValue(name, out object? raw) || raw == null)
                {
                    continue;
                }

                if (raw is bool boolValue)
                {
                    value = boolValue;
                    return true;
                }

                if (bool.TryParse(raw.ToString(), out bool parsed))
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
            string localPath = ApplicationData.Current.LocalFolder.Path;
            if (string.IsNullOrWhiteSpace(localPath))
            {
                throw new InvalidOperationException(L("Database/Debug/LocalStatePathMissing"));
            }

            Directory.CreateDirectory(localPath);
            return localPath;
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
            public string CountryCode { get; set; } = DefaultCountryCode;

            public string DownloadDirectory { get; set; } = string.Empty;

            public bool DetailedIpatoolLogEnabled { get; set; }

            public bool OwnedCheckEnabled { get; set; }

            public bool KeychainPassphraseRotationEnabled { get; set; } = true;

            public string IpatoolFlavor { get; set; } = IpatoolFlavorMain;

            public string CustomIpatoolPath { get; set; } = string.Empty;
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }
    }
}
