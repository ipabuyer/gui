using System.Text;

namespace IPAbuyer.Common
{
    public static class UiLogStore
    {
        private const int MaxLogLines = 1000;
        private static readonly object SyncRoot = new();
        private static readonly List<UiLogEntry> Entries = new();

        public static UiLogEntry Append(string message, UiLogLevel level)
        {
            UiLogEntry entry = UiLogFormatter.Build(message, level);
            lock (SyncRoot)
            {
                Entries.Add(entry);
                if (Entries.Count > MaxLogLines)
                {
                    Entries.RemoveAt(0);
                }
            }

            return entry;
        }

        public static UiLogEntry[] GetSnapshot()
        {
            lock (SyncRoot)
            {
                return Entries.ToArray();
            }
        }

        public static string GetText()
        {
            lock (SyncRoot)
            {
                if (Entries.Count == 0)
                {
                    return string.Empty;
                }

                var builder = new StringBuilder();
                foreach (UiLogEntry entry in Entries)
                {
                    builder.AppendLine(entry.FormattedText);
                }

                return builder.ToString();
            }
        }

        public static void Clear()
        {
            lock (SyncRoot)
            {
                Entries.Clear();
            }
        }
    }
}
