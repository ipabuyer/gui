using IPAbuyer.Core.Configuration;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace IPAbuyer.Core.Integration.Ipatool
{
    internal static class IpatoolCommandBuilder
    {
        private static readonly ResourceLoader Loader = new();

        internal static bool IsLogout(IReadOnlyList<string> arguments)
        {
            return arguments.Count >= 2
                && string.Equals(arguments[0], "auth", StringComparison.OrdinalIgnoreCase)
                && string.Equals(arguments[1], "revoke", StringComparison.OrdinalIgnoreCase);
        }

        internal static string ResolvePassphrase(string? passphrase)
        {
            if (!string.IsNullOrWhiteSpace(passphrase))
            {
                return passphrase.Trim();
            }

            string storedPassphrase = PassphraseStore.Get();
            if (!string.IsNullOrWhiteSpace(storedPassphrase))
            {
                return storedPassphrase;
            }

            throw new InvalidOperationException(Loader.GetString("Ipatool/Error/MissingPassphrase"));
        }

        internal static IReadOnlyList<string> BuildStandardArguments(IReadOnlyList<string> arguments, string passphrase, bool isLogout)
        {
            var finalArguments = new List<string>(arguments);
            if (!isLogout)
            {
                finalArguments.Add("--keychain-passphrase");
                finalArguments.Add(passphrase);
            }

            finalArguments.Add("--format");
            finalArguments.Add("json");
            finalArguments.Add("--non-interactive");
            finalArguments.Add("--verbose");
            return finalArguments;
        }

        internal static IReadOnlyList<string> BuildDownloadArguments(string bundleId, string outputDirectory, string passphrase)
        {
            return new[]
            {
                "download", "--output", outputDirectory, "--bundle-identifier", bundleId, "--purchase",
                "--keychain-passphrase", passphrase, "--format", "json", "--non-interactive", "--verbose"
            };
        }

        internal static IReadOnlyList<string> BuildListPurchasesArguments(int maxResults, int page)
        {
            return new[]
            {
                "list-purchases",
                "--max-results", maxResults.ToString(CultureInfo.InvariantCulture),
                "--page", page.ToString(CultureInfo.InvariantCulture)
            };
        }

        internal static IReadOnlyDictionary<string, string> CreateEnvironmentVariables()
        {
            return new Dictionary<string, string>
            {
                ["NO_COLOR"] = "1",
                ["TERM"] = "dumb"
            };
        }

        internal static string GetSafeCommandLabel(IReadOnlyList<string> arguments)
        {
            return arguments.Count switch
            {
                0 => "ipatool",
                1 => $"ipatool {arguments[0]}",
                _ => $"ipatool {arguments[0]} {arguments[1]}"
            };
        }
    }
}
