namespace IPAbuyer.Core.Configuration
{
    public static class ApplicationSettings
    {
        public static string GetCountryCode() => ConfigurationStore.GetCountryCode();

        public static void SaveCountryCode(string countryCode) => ConfigurationStore.SaveCountryCode(countryCode);

        public static string GetDownloadDirectory() => ConfigurationStore.GetDownloadDirectory();

        public static void SaveDownloadDirectory(string directoryPath) => ConfigurationStore.SaveDownloadDirectory(directoryPath);

        public static string GetDefaultDownloadDirectory() => ConfigurationStore.GetDefaultDownloadDirectory();

        public static bool GetDetailedIpatoolLogEnabled() => ConfigurationStore.GetDetailedIpatoolLogEnabled();

        public static void SaveDetailedIpatoolLogEnabled(bool enabled) => ConfigurationStore.SaveDetailedIpatoolLogEnabled(enabled);

        public static bool GetKeychainPassphraseRotationEnabled() => ConfigurationStore.GetKeychainPassphraseRotationEnabled();

        public static void SaveKeychainPassphraseRotationEnabled(bool enabled) => ConfigurationStore.SaveKeychainPassphraseRotationEnabled(enabled);

        public static bool IsValidCountryCode(string? code) => ConfigurationStore.IsValidCountryCode(code);
    }
}
