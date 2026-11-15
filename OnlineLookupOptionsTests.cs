using Elektra.Configuration;
using Elektra.Knowledge.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elektra.Tests;

[TestClass]
public sealed class OnlineLookupOptionsTests
{
    [TestMethod]
    public void WeatherProvider_WhenOnlineLookupsAreDisabled_StillHandlesLocalTimeButNotWeather()
    {
        var provider = new WatervilleTimeWeatherProvider(
            httpClientFactory: null!,
            NullLogger<WatervilleTimeWeatherProvider>.Instance,
            Options.Create(new AssistantOptions { EnableOnlineLookups = false }));

        Assert.IsTrue(provider.CanHandle("what time is it"));
        Assert.IsFalse(provider.CanHandle("what is the weather"));
    }

    [TestMethod]
    public void WeatherProvider_WhenOnlineLookupsAreEnabled_HandlesWeather()
    {
        var provider = new WatervilleTimeWeatherProvider(
            httpClientFactory: null!,
            NullLogger<WatervilleTimeWeatherProvider>.Instance,
            Options.Create(new AssistantOptions { EnableOnlineLookups = true }));

        Assert.IsTrue(provider.CanHandle("what is the weather"));
    }
}
