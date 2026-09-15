using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Providers;

public class PhishCollectionImageProviderTests
{
    [Theory]
    [InlineData("phish-collection-poster")]
    [InlineData("phish-collection-backdrop")]
    public async Task GetImageResponse_ShouldServeEmbeddedJpeg(string url)
    {
        var provider = new PhishCollectionImageProvider(Mock.Of<ILogger<PhishCollectionImageProvider>>());

        var response = await provider.GetImageResponse(url, CancellationToken.None);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        bytes.Length.Should().BeGreaterThan(1000);
        bytes.Take(3).Should().Equal(new byte[] { 0xFF, 0xD8, 0xFF }, "the embedded resources are JPEG files");
    }
}
