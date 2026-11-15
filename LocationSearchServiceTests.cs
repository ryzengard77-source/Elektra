/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using System.Net;
using System.Text;
using Elektra.Configuration;
using Elektra.Knowledge.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elektra.Tests;

[TestClass]
public sealed class LocationSearchServiceTests
{
    [TestMethod]
    public async Task FindAsync_ForStreetAddressSelectsExactHouseNumberFromCandidates()
    {
        const string responseJson = """
            [
              {"lat":"44.55","lon":"-69.63","display_name":"61 Elm Street, Waterville, Maine, United States","address":{"house_number":"61","road":"Elm Street","city":"Waterville","state":"Maine","country":"United States"}},
              {"lat":"44.56","lon":"-69.64","display_name":"60 Elm Street, Waterville, Maine, United States","address":{"house_number":"60","road":"Elm Street","city":"Waterville","state":"Maine","country":"United States"}}
            ]
            """;
        var handler = new StubHttpMessageHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 512 });
        var service = CreateService(httpClient, cache);

        GeocodedLocation? result = await service.FindAsync("60 Elm Street, Waterville, Maine", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("60 Elm Street, Waterville, Maine, United States", result.DisplayName);
        Assert.AreEqual(44.56, result.Latitude);
        Assert.AreEqual(-69.64, result.Longitude);
        Assert.IsNotNull(handler.RequestUri);
        string query = Uri.UnescapeDataString(handler.RequestUri.Query);
        Assert.Contains("countrycodes=us", query);
        Assert.Contains("limit=5", query);
        Assert.Contains("addressdetails=1", query);
    }

    [TestMethod]
    public async Task FindAsync_ForStreetAddressRejectsCandidatesWithDifferentHouseNumber()
    {
        const string responseJson = """
            [
              {"lat":"44.55","lon":"-69.63","display_name":"61 Elm Street, Waterville, Maine, United States","address":{"house_number":"61","road":"Elm Street","city":"Waterville","state":"Maine","country":"United States"}}
            ]
            """;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(responseJson));
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 512 });
        var service = CreateService(httpClient, cache);

        GeocodedLocation? result = await service.FindAsync("60 Elm Street, Waterville, Maine", CancellationToken.None);

        Assert.IsNull(result);
    }

    private static LocationSearchService CreateService(HttpClient httpClient, IMemoryCache cache) => new(
        httpClient,
        cache,
        Options.Create(new AssistantOptions { LocationSearchEndpoint = "https://geocode.example/search" }),
        NullLogger<LocationSearchService>.Instance);

    private sealed class StubHttpMessageHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
