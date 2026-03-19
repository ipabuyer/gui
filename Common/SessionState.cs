using System;

namespace IPAbuyer.Common
{
    public static class SessionState
    {
        private static readonly object _syncRoot = new();
        private static string _currentAccount = string.Empty;
        private static bool _isLoggedIn;
        private static bool _isMockAccount;
        private static bool _initialized;
        public static event Action? LoginStateChanged;

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

        public static bool IsMockAccount
        {
            get
            {
                lock (_syncRoot)
                {
                    EnsureInitialized();
                    return _isMockAccount;
                }
            }
        }

        public static void SetLoginState(string account, bool isLoggedIn, bool isMockAccount = false)
        {
            string normalizedAccount = account?.Trim() ?? string.Empty;

            bool changed;
            lock (_syncRoot)
            {
                EnsureInitialized();
                changed = !string.Equals(_currentAccount, normalizedAccount, StringComparison.Ordinal)
                    || _isLoggedIn != isLoggedIn
                    || _isMockAccount != isMockAccount;
                _currentAccount = normalizedAccount;
                _isLoggedIn = isLoggedIn;
                _isMockAccount = isMockAccount;
            }

            if (changed)
            {
                LoginStateChanged?.Invoke();
            }
        }

        public static void Reset()
        {
            bool changed;
            lock (_syncRoot)
            {
                EnsureInitialized();
                changed = _isLoggedIn
                    || !string.IsNullOrWhiteSpace(_currentAccount)
                    || _isMockAccount;
                _isLoggedIn = false;
                _currentAccount = string.Empty;
                _isMockAccount = false;
            }

            if (changed)
            {
                LoginStateChanged?.Invoke();
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            KeychainConfig.InitializeDatabase();
            _currentAccount = string.Empty;
            _isLoggedIn = false;
            _isMockAccount = false;
            _initialized = true;
        }
    }
}
