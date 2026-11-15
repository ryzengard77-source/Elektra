/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Elektra.Configuration;
using Elektra.Knowledge.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elektra.Knowledge.Providers;

public sealed class WatervilleTimeWeatherProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<WatervilleTimeWeatherProvider> logger,
    IOptions<AssistantOptions> options) : IRealTimeKnowledgeProvider
{
    private const string LocationName = "Waterville, Maine";
    private const string WeatherUrl =
        "https://api.open-meteo.com/v1/forecast?latitude=44.5520&longitude=-69.6317" +
        "&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m" +
        "&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch&timezone=America%2FNew_York";

    private static readonly TimeZoneInfo EasternTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public string ProviderName => "Waterville local time and Open-Meteo";

    public bool CanHandle(string query) => IsTimeOrDateQuestion(query) ||
        (options.Value.EnableOnlineLookups && IsWeatherQuestion(query));

    public async Task<QueryResponse?> TryResolveAsync(string query, CancellationToken ct)
    {
        if (IsTimeOrDateQuestion(query))
        {
            DateTimeOffset localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, EasternTimeZone);
            string zoneName = EasternTimeZone.IsDaylightSavingTime(localNow)
                ? EasternTimeZone.DaylightName
                : EasternTimeZone.StandardName;
            string answer = $"It is {localNow.ToString("h:mm:ss tt", CultureInfo.InvariantCulture)} " +
                $"{zoneName} in {LocationName}. Today is {localNow.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)}.";
            return new QueryResponse(query, answer, "Local clock (Eastern Time)");
        }

        if (!options.Value.EnableOnlineLookups || !IsWeatherQuestion(query))
            return null;

        try
        {
            using HttpClient client = httpClientFactory.CreateClient("WatervilleWeather");
            using HttpResponseMessage response = await client.GetAsync(WeatherUrl, ct);
            response.EnsureSuccessStatusCode();

            await using Stream content = await response.Content.ReadAsStreamAsync(ct);
            using JsonDocument document = await JsonDocument.ParseAsync(content, cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("current", out JsonElement current) ||
                !TryGetDouble(current, "temperature_2m", out double temperature) ||
                !TryGetDouble(current, "apparent_temperature", out double feelsLike) ||
                !TryGetDouble(current, "relative_humidity_2m", out double humidity) ||
                !TryGetDouble(current, "wind_speed_10m", out double windSpeed) ||
                !TryGetInt32(current, "weather_code", out int weatherCode) ||
                !current.TryGetProperty("time", out JsonElement timeElement) ||
                !DateTime.TryParse(timeElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime observationTime))
            {
                throw new JsonException("The weather response did not contain all required current conditions.");
            }

            string description = DescribeWeatherCode(weatherCode);
            string answer = $"Currently in {LocationName}, it is {description} and {temperature:0.#} degrees Fahrenheit, " +
                $"feeling like {feelsLike:0.#}. Humidity is {humidity:0}% and wind is {windSpeed:0.#} miles per hour. " +
                $"Conditions are as of {observationTime.ToString("h:mm tt", CultureInfo.InvariantCulture)} Eastern Time.";
            return new QueryResponse(query, answer, "Open-Meteo current weather");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not retrieve current weather for {Location}.", LocationName);
            return new QueryResponse(
                query,
                $"I couldn't retrieve current weather for {LocationName} right now. Please try again shortly.",
                "Weather service unavailable");
        }
    }

    private static bool IsTimeOrDateQuestion(string query)
    {
        string normalized = NormalizeQuery(query);
        return normalized is "time" or "date" or "what time is it" or "what is the time" or
            "what's the time" or "tell me the time" or "current time" or "what is today's date" or
            "what's today's date" or "what is the date" or "what's the date" or "what date is it" or
            "what date is today" or "what day is it" or "what is the current date" or "current date" or "today's date" or
            "today's day" or "what day is today";
    }

    private static bool IsWeatherQuestion(string query)
    {
        string normalized = NormalizeQuery(query);
        return normalized.Contains("weather", StringComparison.Ordinal) ||
            normalized.Contains("forecast", StringComparison.Ordinal) ||
            normalized is "is it raining" or "is it raining outside" or "is it sunny" or "is it cloudy" or
                "is it snowing" or "is it windy" or "is it cold outside" or "is it hot outside" or
                "what is the temperature outside" or "what's the temperature outside" or
                "what is the temperature" or "what's the temperature" or "temperature outside" or
                "how hot is it outside" or "how cold is it outside";
    }

    private static string NormalizeQuery(string query) =>
        string.Join(' ', query.Trim().ToLowerInvariant().Replace('’', '\'')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .TrimEnd('?', '.', '!');

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetDouble(out value))
            return true;

        value = 0;
        return false;
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt32(out value))
            return true;

        value = 0;
        return false;
    }

    private static string DescribeWeatherCode(int code) => code switch
    {
        0 => "clear skies",
        1 => "mostly clear skies",
        2 => "partly cloudy skies",
        3 => "overcast skies",
        45 or 48 => "foggy",
        51 => "light drizzle",
        53 => "drizzle",
        55 => "heavy drizzle",
        56 or 57 => "freezing drizzle",
        61 => "light rain",
        63 => "rain",
        65 => "heavy rain",
        66 or 67 => "freezing rain",
        71 => "light snow",
        73 => "snow",
        75 => "heavy snow",
        77 => "snow grains",
        80 => "light rain showers",
        81 => "rain showers",
        82 => "heavy rain showers",
        85 => "light snow showers",
        86 => "heavy snow showers",
        95 => "a thunderstorm",
        96 or 99 => "a thunderstorm with hail",
        _ => "current conditions"
    };
}
