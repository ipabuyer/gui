using System;

namespace IPAbuyer.Common
{
    public static class SessionState
    {
    private static readonly object _syncRoot = new();
    private static string _currentAccount = string.Empty;
    private static bool _isLoggedIn;
    private static bool _initialized;

        public static string CurrentAccount
        {
            get
            {
                lock (_syncRoot)
                {
                    EnsureInitialized();
                    return _currentAccount;
                }
            }
        }

        public static bool IsLoggedIn
        {
            get
            {
                lock (_syncRoot)
                {
                    EnsureInitialized();
                    return _isLoggedIn;
                }
            }
        }

        public static void SetLoginState(string account, bool isLoggedIn)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                throw new ArgumentException("account 不能为空", nameof(account));
            }

            lock (_syncRoot)
            {
                EnsureInitialized();
                _currentAccount = account;
                _isLoggedIn = isLoggedIn;
            }
        }

        public static void Reset()
        {
            lock (_syncRoot)
            {
                EnsureInitialized();
                _isLoggedIn = false;
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            KeychainConfig.InitializeDatabase();
            _currentAccount = KeychainConfig.GetLastLoginUsername() ?? string.Empty;
            _initialized = true;
        }
    }
}
