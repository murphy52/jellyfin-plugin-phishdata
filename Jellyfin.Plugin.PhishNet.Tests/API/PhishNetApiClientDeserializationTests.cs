using System.Net;
using System.Text;
using FluentAssertions;
using Jellyfin.Plugin.PhishNet.API.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.API;

/// <summary>
/// Guards against the live Phish.net v5 payload shapes: numeric fields where the
/// model declares strings, and the venue endpoint's venuename/venuenotes keys.
/// </summary>
public class PhishNetApiClientDeserializationTests
{
    private static PhishNetApiClient CreateClient(string responseJson)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.phish.net/v5/") };
        return new PhishNetApiClient(httpClient, Mock.Of<ILogger<PhishNetApiClient>>(), "test-key");
    }

    [Fact]
    public async Task GetShowsAsync_ShouldParseNumericShowYear()
    {
        const string json = """
        {"error":false,"error_message":"","data":[{"showid":1710612105,"showyear":2024,"showmonth":8,"showday":30,
        "showdate":"2024-08-30","permalink":"https://phish.net/setlists/x.html","exclude_from_stats":0,"venueid":1043,
        "setlist_notes":"","venue":"Dick's Sporting Goods Park","city":"Commerce City","state":"CO","country":"USA",
        "artistid":1,"artist_name":"Phish","tourid":218,"tour_name":"2024 Summer Tour"}]}
        """;

        var shows = await CreateClient(json).GetShowsAsync("2024-08-30");

        shows.Should().ContainSingle();
        shows[0].ShowYear.Should().Be("2024");
        shows[0].Venue.Should().Be("Dick's Sporting Goods Park");
        shows[0].VenueId.Should().Be(1043);
    }

    [Fact]
    public async Task GetVenueAsync_ShouldMapVenueNameAndNotes()
    {
        const string json = """
        {"error":false,"error_message":"","data":[{"venueid":1043,"venuename":"Dick's Sporting Goods Park",
        "city":"Commerce City","state":"CO","country":"USA","venuenotes":"Home of the Colorado Rapids.","alias":0,"short_name":"Dick's"}]}
        """;

        var venue = await CreateClient(json).GetVenueAsync(1043);

        venue.Should().NotBeNull();
        venue!.Name.Should().Be("Dick's Sporting Goods Park");
        venue.VenueInfo.Should().Be("Home of the Colorado Rapids.");
        venue.City.Should().Be("Commerce City");
    }
}
