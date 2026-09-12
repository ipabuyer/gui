using IPAbuyer.Core.Services.Purchases;
using Xunit;

namespace IPAbuyer.Tests.Services.Purchases
{
    public sealed class OwnedAppsPageParserTests
    {
        [Fact]
        public void Parse_SuccessPayload_ReturnsBundleIdsAndTotalCount()
        {
            string payload = """
                {"level":"info","count":2,"totalCount":1753,"page":1,"apps":[
                {"id":1487448370,"bundleID":"com.ingka.ikea.app.cn.prod","name":"IKEA 宜家家居","version":"5.19.0","price":0,"purchaseDate":"2026-09-11T11:59:20Z"},
                {"id":342576766,"bundleID":"com.amazon.AmazonCN","name":"亚马逊购物","version":"26.18","price":0,"purchaseDate":"2026-09-11T11:59:03Z"}],
                "time":"2026-09-12T10:24:26+08:00"}
                """;

            OwnedAppsPage page = OwnedAppsPageParser.Parse(payload);

            Assert.True(page.Success);
            Assert.Equal(1753, page.TotalCount);
            Assert.Equal(2, page.BundleIds.Count);
            Assert.Contains("com.ingka.ikea.app.cn.prod", page.BundleIds);
            Assert.Contains("com.amazon.AmazonCN", page.BundleIds);
        }

        [Fact]
        public void Parse_EmptyAppsPayload_ReturnsSuccessWithNoBundleIds()
        {
            string payload = """
                {"level":"info","count":0,"totalCount":1753,"page":999,"apps":[],"time":"2026-09-12T10:25:31+08:00"}
                """;

            OwnedAppsPage page = OwnedAppsPageParser.Parse(payload);

            Assert.True(page.Success);
            Assert.Equal(1753, page.TotalCount);
            Assert.Empty(page.BundleIds);
        }

        [Fact]
        public void Parse_ErrorPayload_ReturnsFailureWithMessage()
        {
            string payload = """
                {"level":"error","error":"max results must not exceed 100","success":false,"time":"2026-09-12T10:24:49+08:00"}
                """;

            OwnedAppsPage page = OwnedAppsPageParser.Parse(payload);

            Assert.False(page.Success);
            Assert.Equal("max results must not exceed 100", page.ErrorMessage);
            Assert.Empty(page.BundleIds);
        }

        [Fact]
        public void Parse_WrongPassphraseError_ReturnsFailureWithUnderlyingMessage()
        {
            string payload = """
                {"level":"error","error":"failed to get account: failed to get item: aes.KeyUnwrap(): integrity check failed.","success":false,"time":"2026-09-12T10:24:49+08:00"}
                """;

            OwnedAppsPage page = OwnedAppsPageParser.Parse(payload);

            Assert.False(page.Success);
            Assert.Contains("KeyUnwrap", page.ErrorMessage);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("not json at all")]
        public void Parse_InvalidPayload_ReturnsFailure(string? payload)
        {
            OwnedAppsPage page = OwnedAppsPageParser.Parse(payload);

            Assert.False(page.Success);
            Assert.Empty(page.BundleIds);
        }
    }
}
