using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Elektra.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elektra.Knowledge.Providers;

public sealed record GeocodedLocation(string DisplayName, double Latitude, double Longitude);

public sealed class LocationSearchService
{
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LocationSearchService> _logger;
    private readonly Uri _endpoint;

    public LocationSearchService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<AssistantOptions> options,
        ILogger<LocationSearchService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        if (!Uri.TryCreate(options.Value.LocationSearchEndpoint, UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidOperationException("Assistant:LocationSearchEndpoint must be an HTTPS geocoding endpoint without a query or fragment.");
        }
        _endpoint = endpoint;
    }

    public async Task<GeocodedLocation?> FindAsync(string place, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(place))
            throw new ArgumentException("A place name or address is required.", nameof(place));
        if (place.Length > 200)
            throw new ArgumentException("Place searches must be 200 characters or fewer.", nameof(place));

        string normalizedPlace = place.Trim();
        string cacheKey = $"location-search:{normalizedPlace.ToUpperInvariant()}";
        if (_cache.TryGetValue(cacheKey, out GeocodedLocation? cached))
            return cached;

        await RequestGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached))
                return cached;

            TimeSpan sinceLastRequest = DateTimeOffset.UtcNow - _lastRequestAt;
            if (sinceLastRequest < TimeSpan.FromSeconds(1))
                await Task.Delay(TimeSpan.FromSeconds(1) - sinceLastRequest, ct).ConfigureAwait(false);

            _lastRequestAt = DateTimeOffset.UtcNow;
            string? requestedHouseNumber = GetLeadingHouseNumber(normalizedPlace);
            string countryFilter = requestedHouseNumber == null ? string.Empty : "&countrycodes=us";
            string requestUrl = $"{_endpoint.AbsoluteUri}?q={Uri.EscapeDataString(normalizedPlace)}&format=jsonv2&limit=5&addressdetails=1{countryFilter}";
            using var response = await _httpClient.GetAsync(
                requestUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
                return null;

            foreach (JsonElement candidate in document.RootElement.EnumerateArray())
            {
                if (!TryReadCoordinate(candidate, "lat", out double latitude) || latitude is < -90 or > 90 ||
                    !TryReadCoordinate(candidate, "lon", out double longitude) || longitude is < -180 or > 180 ||
                    !candidate.TryGetProperty("display_name", out JsonElement nameElement) ||
                    nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string displayName = nameElement.GetString() ?? normalizedPlace;
                if (requestedHouseNumber != null && !HasMatchingHouseNumber(candidate, requestedHouseNumber))
                    continue;

                var result = new GeocodedLocation(displayName, latitude, longitude);
                _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });
                _logger.LogDebug("Resolved a {QueryLength}-character place search to coordinates.", normalizedPlace.Length);
                return result;
            }

            if (requestedHouseNumber != null)
                _logger.LogInformation("No US geocoding result matched the spoken street number for a {QueryLength}-character address.", normalizedPlace.Length);
            return null;
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private static string? GetLeadingHouseNumber(string place)
    {
        ReadOnlySpan<char> remaining = place.AsSpan().TrimStart();
        int length = 0;
        while (length < remaining.Length && (char.IsDigit(remaining[length]) || remaining[length] is '-' or '/'))
            length++;
        return length > 0 && remaining[..length].IndexOfAnyInRange('0', '9') >= 0
            ? remaining[..length].ToString()
            : null;
    }

    private static bool HasMatchingHouseNumber(JsonElement candidate, string houseNumber) =>
        candidate.TryGetProperty("address", out JsonElement address) &&
        address.ValueKind == JsonValueKind.Object &&
        address.TryGetProperty("house_number", out JsonElement resultNumber) &&
        resultNumber.ValueKind == JsonValueKind.String &&
        string.Equals(resultNumber.GetString(), houseNumber, StringComparison.OrdinalIgnoreCase);

    private static bool TryReadCoordinate(JsonElement element, string propertyName, out double coordinate)
    {
        coordinate = 0;
        return element.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out coordinate) &&
            double.IsFinite(coordinate);
    }
}
