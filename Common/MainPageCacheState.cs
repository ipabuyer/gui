namespace IPAbuyer.Common
{
    public static class MainPageCacheState
    {
        private static bool _searchCacheInvalidated;
        private static readonly object _syncRoot = new();

        public static void InvalidateSearchCache()
        {
            lock (_syncRoot)
            {
                _searchCacheInvalidated = true;
            }
        }

        public static bool ConsumeSearchCacheInvalidation()
        {
            lock (_syncRoot)
            {
                if (!_searchCacheInvalidated)
                {
                    return false;
                }

                _searchCacheInvalidated = false;
                return true;
            }
        }
    }
}
