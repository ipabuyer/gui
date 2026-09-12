using IPAbuyer.Core.Logging;
using Xunit;

namespace IPAbuyer.Tests.Logging
{
    public sealed class UiLogFormatterTests
    {
        [Theory]
        [InlineData(UiLogLevel.Info, "INFO")]
        [InlineData(UiLogLevel.Tip, "TIP")]
        [InlineData(UiLogLevel.Success, "SUCCESS")]
        [InlineData(UiLogLevel.Error, "ERROR")]
        [InlineData(UiLogLevel.Ipatool, "ipatool")]
        public void Build_UsesLevelTag(UiLogLevel level, string tag)
        {
            UiLogEntry entry = UiLogFormatter.Build("具体日志", level);

            Assert.Matches(
                @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] \[" + tag + @"\] 具体日志$",
                entry.FormattedText);
            Assert.Equal(level, entry.Level);
        }

        [Fact]
        public void Build_TrimsMessageWhitespace()
        {
            UiLogEntry entry = UiLogFormatter.Build("  hello world  ", UiLogLevel.Info);

            Assert.Matches(@"\[INFO\] hello world$", entry.FormattedText);
        }

        [Fact]
        public void Build_NullMessageBecomesEmptyBody()
        {
            UiLogEntry entry = UiLogFormatter.Build(null!, UiLogLevel.Error);

            Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] \[ERROR\] $", entry.FormattedText);
        }
    }
}
