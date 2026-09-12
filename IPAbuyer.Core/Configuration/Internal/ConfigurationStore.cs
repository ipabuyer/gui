using Microsoft.Windows.ApplicationModel.Resources;
using System.Text;
using System.Text.Json;
using Windows.Security.Credentials;
using Windows.Storage;
using Windows.System.UserProfile;

namespace IPAbuyer.Core.Configuration
{
    /// <summary>
    /// 本地配置读写（LocalSettings 版，不使用 KeychainConfig.db）
    /// </summary>
    internal static class ConfigurationStore
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
        private const string KeychainPassphraseRotationEnabledSettingKey = "KeychainPassphraseRotationEnabled";
        internal const string IpatoolFlavorMain = "Main";
        internal const string IpatoolFlavorCustom = "Custom";
        private const string IpatoolFlavorSettingKey = "IpatoolFlavor";
        private const string CustomIpatoolPathSettingKey = "CustomIpatoolPath";
        private static readonly object SyncRoot = new();


        internal static void InitializeDatabase()
        {
            lock (SyncRoot)
            {
                EnsureStorageReady();
                RemoveLegacyKeychainDatabase();
                var values = ApplicationData.Current.LocalSettings.Values;
                bool hasSavedCountryCode = values.ContainsKey(CountryCodeSettingKey);
                bool hasMigratedSettings = TryMigrateLegacySettingsFile();
                InitializeCountryCodeIfMissing(hasSavedCountryCode || hasMigratedSettings);
                _ = LoadSettingsInternal();
            }
        }

        internal static string GetCountryCode(string? account = null)
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

        internal static void SaveCountryCode(string countryCode, string? account = null)
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

        internal static string GetDownloadDirectory()
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

        internal static void SavePassphrase(string passphrase)
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

        internal static string? GetPassphrase(string? account)
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

        internal static string GetDefaultPassphrase()
        {
            return GetPassphrase(null) ?? CreateDefaultPassphrase();
        }

        internal static string RotateDefaultPassphrase()
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

        internal static void SaveDownloadDirectory(string directoryPath)
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

        internal static bool GetDetailedIpatoolLogEnabled()
        {
            lock (SyncRoot)
            {
                return LoadSettingsInternal().DetailedIpatoolLogEnabled;
            }
        }

        internal static void SaveDetailedIpatoolLogEnabled(bool enabled)
        {
            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.DetailedIpatoolLogEnabled = enabled;
                SaveSettingsInternal(settings);
            }
        }

        internal static bool GetKeychainPassphraseRotationEnabled()
        {
            lock (SyncRoot)
            {
                return LoadSettingsInternal().KeychainPassphraseRotationEnabled;
            }
        }

        internal static void SaveKeychainPassphraseRotationEnabled(bool enabled)
        {
            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.KeychainPassphraseRotationEnabled = enabled;
                SaveSettingsInternal(settings);
            }
        }

        internal static string GetIpatoolFlavor()
        {
            lock (SyncRoot)
            {
                return LoadSettingsInternal().IpatoolFlavor;
            }
        }

        internal static void SaveIpatoolFlavor(string flavor)
        {
            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.IpatoolFlavor = NormalizeIpatoolFlavor(flavor);
                SaveSettingsInternal(settings);
            }
        }

        internal static string GetCustomIpatoolPath()
        {
            lock (SyncRoot)
            {
                return LoadSettingsInternal().CustomIpatoolPath;
            }
        }

        internal static bool HasUsableCustomIpatoolPath()
        {
            lock (SyncRoot)
            {
                string path = LoadSettingsInternal().CustomIpatoolPath;
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            }
        }

        internal static void SaveCustomIpatoolPath(string path)
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
                settings.IpatoolFlavor = IpatoolFlavorCustom;
                SaveSettingsInternal(settings);
            }
        }

        internal static void DeleteCustomIpatoolPath()
        {
            lock (SyncRoot)
            {
                var settings = LoadSettingsInternal();
                settings.CustomIpatoolPath = string.Empty;
                settings.IpatoolFlavor = IpatoolFlavorMain;
                SaveSettingsInternal(settings);
            }
        }

        internal static bool IsValidCountryCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            return AppleStorefrontCatalog.Contains(code);
        }

        internal static bool IsMockAccount(string? username, string? password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            return string.Equals(username.Trim(), "test", StringComparison.OrdinalIgnoreCase)
                && string.Equals(password.Trim(), "test", StringComparison.Ordinal);
        }

        internal static string GetDefaultDownloadDirectory()
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

        private static void InitializeCountryCodeIfMissing(bool hasSavedCountryCode)
        {
            if (hasSavedCountryCode)
            {
                return;
            }

            string countryCode = ResolveInitialCountryCodeFromWindows();
            ApplicationData.Current.LocalSettings.Values[CountryCodeSettingKey] = countryCode;
        }

        private static string ResolveInitialCountryCodeFromWindows()
        {
            try
            {
                return ResolveInitialCountryCode(GlobalizationPreferences.HomeGeographicRegion);
            }
            catch
            {
                return DefaultCountryCode;
            }
        }

        internal static string ResolveInitialCountryCode(string? homeGeographicRegion)
        {
            if (string.IsNullOrWhiteSpace(homeGeographicRegion))
            {
                return DefaultCountryCode;
            }

            string normalized = homeGeographicRegion.Trim().ToLowerInvariant();
            return AppleStorefrontCatalog.Contains(normalized) ? normalized : DefaultCountryCode;
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

        private static bool TryMigrateLegacySettingsFile()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.ContainsKey(CountryCodeSettingKey))
            {
                return true;
            }

            string path = GetSettingsFilePath();
            if (!File.Exists(path))
            {
                return false;
            }

            LocalSettingsModel model;
            bool hasCountryCode;
            try
            {
                (model, hasCountryCode) = ReadLegacySettingsFile(path);
                if (!hasCountryCode)
                {
                    model.CountryCode = ResolveInitialCountryCodeFromWindows();
                }

                NormalizeSettings(model);
            }
            catch
            {
                return false;
            }

            SaveSettingsInternal(model);
            MarkLegacySettingsFileMigrated(path);
            return true;
        }

        private static void SaveSettingsInternal(LocalSettingsModel settings)
        {
            NormalizeSettings(settings);
            var values = ApplicationData.Current.LocalSettings.Values;
            values[CountryCodeSettingKey] = settings.CountryCode;
            values[DownloadDirectorySettingKey] = settings.DownloadDirectory;
            values[DetailedIpatoolLogEnabledSettingKey] = settings.DetailedIpatoolLogEnabled;
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
            values.Remove("OwnedCheckEnabled");
            values.Remove("keychain_passphrase_rotation");
            values.Remove("AuthIpatoolFlavor");
            values.Remove("auth_ipatool_flavor");
            values.Remove("custom_ipatool_path");
        }

        private static (LocalSettingsModel Model, bool HasCountryCode) ReadLegacySettingsFile(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            var model = CreateDefaultSettings();
            if (string.IsNullOrWhiteSpace(json))
            {
                return (model, false);
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            bool hasCountryCode = TryReadStringProperty(root, out string? countryValue, "country", "CountryCode");
            if (hasCountryCode)
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

            return (model, hasCountryCode);
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
                KeychainPassphraseRotationEnabled = true,
                IpatoolFlavor = IpatoolFlavorMain,
                CustomIpatoolPath = string.Empty
            };
        }

        private static string NormalizeIpatoolFlavor(string? flavor)
        {
            return string.Equals(flavor?.Trim(), IpatoolFlavorCustom, StringComparison.OrdinalIgnoreCase)
                ? IpatoolFlavorCustom
                : IpatoolFlavorMain;
        }

        private static bool TryReadStringProperty(JsonElement root, out string? value, params string[] names)
        {
            foreach (string name in names)
            {
                if (TryGetJsonProperty(root, name, out JsonElement token)
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
                if (!TryGetJsonProperty(root, name, out JsonElement token)
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

        private static bool TryGetJsonProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
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
