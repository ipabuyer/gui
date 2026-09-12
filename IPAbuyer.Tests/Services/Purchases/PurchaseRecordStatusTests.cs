using IPAbuyer.Core.Services.Purchases;
using Xunit;

namespace IPAbuyer.Tests.Services.Purchases
{
    public sealed class PurchaseRecordStatusTests
    {
        [Theory]
        [InlineData("purchased", "purchased")]
        [InlineData("Purchased", "purchased")]
        [InlineData("已购买", "purchased")]
        [InlineData("owned", "purchased")]
        [InlineData("Already owned", "purchased")]
        [InlineData("已拥有", "purchased")]
        public void TryNormalize_AcceptsCanonicalAndLegacyPurchaseRecords(string status, string expected)
        {
            bool normalized = PurchaseRecordStatus.TryNormalize(status, out string actual);

            Assert.True(normalized);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("Not purchased")]
        [InlineData("available for purchase")]
        [InlineData("Unable to purchase")]
        [InlineData("未购买")]
        [InlineData("可购买")]
        [InlineData("无法购买")]
        [InlineData("unknown")]
        public void TryNormalize_RejectsNonRecordStatuses(string? status)
        {
            bool normalized = PurchaseRecordStatus.TryNormalize(status, out string actual);

            Assert.False(normalized);
            Assert.Equal(string.Empty, actual);
        }

        [Theory]
        [InlineData("  purchased  ", "purchased")]
        [InlineData("OWNED", "purchased")]
        [InlineData(" ALREADY OWNED ", "purchased")]
        [InlineData(" 已购买 ", "purchased")]
        [InlineData(" 已拥有 ", "purchased")]
        public void TryNormalize_TrimsWhitespaceAndIgnoresCase(string status, string expected)
        {
            bool normalized = PurchaseRecordStatus.TryNormalize(status, out string actual);

            Assert.True(normalized);
            Assert.Equal(expected, actual);
        }
    }
}
