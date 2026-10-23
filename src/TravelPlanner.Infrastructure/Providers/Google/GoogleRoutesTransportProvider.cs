using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Infrastructure.Providers.Google;

public sealed class GoogleRoutesOptions
{
    public const string SectionName = "Providers:GoogleRoutes";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://routes.googleapis.com";
    /// <summary>Server-side key restricted to the Routes API (never sent to the browser).</summary>
    public string? ApiKey { get; set; }

    public bool IsConfigured => Enabled && ProviderHttp.HasValue(ApiKey);
}

public static class GoogleRoutesMapper
{
    public const string ProviderName = "Google Maps Routes";

    public const string TransitFieldMask =
        "routes.duration,routes.distanceMeters,routes.travelAdvisory.transitFare," +
        "routes.legs.steps.transitDetails.transitLine.nameShort,routes.legs.steps.transitDetails.transitLine.name," +
        "routes.legs.steps.transitDetails.transitLine.vehicle.type";

    public const string DriveFieldMask = "routes.duration,routes.distanceMeters";

    public static TransferOption? MapTransit(JsonElement root)
    {
        if (!TryFirstRoute(root, out var route)) return null;
        var lines = new List<string>();
        if (route.TryGetProperty("legs", out var legs))
        {
            foreach (var leg in legs.EnumerateArray())
            {
                if (!leg.TryGetProperty("steps", out var steps)) continue;
                foreach (var step in steps.EnumerateArray())
                {
                    if (!step.TryGetProperty("transitDetails", out var td) || !td.TryGetProperty("transitLine", out var line)) continue;
                    var name = line.TryGetProperty("nameShort", out var ns) ? ns.GetString() : line.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var vehicle = line.TryGetProperty("vehicle", out var v) && v.TryGetProperty("type", out var t) ? t.GetString() : null;
                    var label = string.Join(" ", new[] { Humanize(vehicle), name }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (label.Length > 0 && !lines.Contains(label)) lines.Add(label);
                }
            }
        }

        decimal? fare = null;
        string? fareCurrency = null;
        if (route.TryGetProperty("travelAdvisory", out var advisory) && advisory.TryGetProperty("transitFare", out var money))
        {
            fareCurrency = money.TryGetProperty("currencyCode", out var cc) ? cc.GetString() : null;
            var units = money.TryGetProperty("units", out var u) ? decimal.Parse(u.ValueKind == JsonValueKind.String ? u.GetString()! : u.GetRawText(), CultureInfo.InvariantCulture) : 0m;
            var nanos = money.TryGetProperty("nanos", out var na) ? na.GetDecimal() : 0m;
            fare = units + nanos / 1_000_000_000m;
        }

        return new TransferOption
        {
            Mode = TransferMode.PublicTransport,
            Title = T.Get("transfer.publicTransport"),
            Provider = ProviderName,
            DistanceMeters = ReadInt(route, "distanceMeters"),
            Duration = ReadDuration(route),
            EstimatedCost = fare,
            CostCurrency = fareCurrency,
            CostIsPerPerson = true,
            CostVerification = fare.HasValue ? VerificationStatus.Estimate : VerificationStatus.Unavailable,
            InfoVerification = VerificationStatus.Estimate,
            Lines = lines,
            Notes = T.Get(fare.HasValue ? "transfer.transitFareNotes" : "transfer.transitNoFareNotes")
        };
    }

    public static TransferOption? MapDrive(JsonElement root)
    {
        if (!TryFirstRoute(root, out var route)) return null;
        return new TransferOption
        {
            Mode = TransferMode.Drive,
            Title = T.Get("transfer.byRoad"),
            Provider = ProviderName,
            DistanceMeters = ReadInt(route, "distanceMeters"),
            Duration = ReadDuration(route),
            CostVerification = VerificationStatus.Unavailable,
            InfoVerification = VerificationStatus.Estimate,
            Notes = T.Get("transfer.driveNotes")
        };
    }

    private static bool TryFirstRoute(JsonElement root, out JsonElement route)
    {
        route = default;
        if (!root.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array || routes.GetArrayLength() == 0) return false;
        route = routes[0];
        return true;
    }

    private static int? ReadInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    /// <summary>Durations are strings such as "1834s".</summary>
    private static TimeSpan? ReadDuration(JsonElement el)
    {
        if (!el.TryGetProperty("duration", out var v) || v.ValueKind != JsonValueKind.String) return null;
        var s = v.GetString()!.TrimEnd('s');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? TimeSpan.FromSeconds(seconds) : null;
    }

    private static string? Humanize(string? vehicleType) => vehicleType switch
    {
        null => null,
        "HEAVY_RAIL" or "COMMUTER_TRAIN" or "HIGH_SPEED_TRAIN" or "LONG_DISTANCE_TRAIN" or "RAIL" => T.Get("vehicle.Train"),
        "SUBWAY" or "METRO_RAIL" => T.Get("vehicle.Metro"),
        "BUS" or "INTERCITY_BUS" or "TROLLEYBUS" => T.Get("vehicle.Bus"),
        "TRAM" or "LIGHT_RAIL" => T.Get("vehicle.Tram"),
        "FERRY" => T.Get("vehicle.Ferry"),
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(vehicleType.ToLowerInvariant().Replace('_', ' '))
    };
}

/// <summary>Google Maps Platform Routes API (POST /directions/v2:computeRoutes) for transit and driving estimates.</summary>
public sealed class GoogleRoutesTransportProvider(
    HttpClient httpClient,
    IOptions<GoogleRoutesOptions> options,
    ILogger<GoogleRoutesTransportProvider> logger) : ITransportSearchProvider
{
    public string Name => GoogleRoutesMapper.ProviderName;
    public bool IsDemo => false;

    public Task<ProviderResult<IReadOnlyList<TransferOption>>> SearchAsync(TransportSearchRequest request, CancellationToken cancellationToken) =>
        ProviderHttp.Guard(Name, async () =>
        {
            var options = new List<TransferOption>();
            var transit = await ComputeAsync(request, "TRANSIT", GoogleRoutesMapper.TransitFieldMask, cancellationToken);
            var drive = await ComputeAsync(request, "DRIVE", GoogleRoutesMapper.DriveFieldMask, cancellationToken);

            if (transit.Body is { } t && GoogleRoutesMapper.MapTransit(t) is { } transitOption) options.Add(transitOption);
            if (drive.Body is { } d && GoogleRoutesMapper.MapDrive(d) is { } driveOption) options.Add(driveOption);

            if (options.Count == 0 && (transit.Error ?? drive.Error) is { } error)
                return ProviderResult<IReadOnlyList<TransferOption>>.Failure(Name, error, "Google Routes request failed.");
            return ProviderResult<IReadOnlyList<TransferOption>>.Success(Name, options);
        }, cancellationToken);

    private async Task<(JsonElement? Body, ProviderErrorKind? Error)> ComputeAsync(TransportSearchRequest r, string mode, string fieldMask, CancellationToken ct)
    {
        object Point(double lat, double lng) => new { location = new { latLng = new { latitude = lat, longitude = lng } } };
        var payload = new Dictionary<string, object>
        {
            ["origin"] = Point(r.AirportLatitude, r.AirportLongitude),
            ["destination"] = Point(r.AccommodationLatitude, r.AccommodationLongitude),
            ["travelMode"] = mode,
            ["languageCode"] = "en"
        };
        if (mode == "DRIVE") payload["routingPreference"] = "TRAFFIC_UNAWARE";

        using var message = new HttpRequestMessage(HttpMethod.Post, "directions/v2:computeRoutes") { Content = JsonContent.Create(payload) };
        message.Headers.Add("X-Goog-Api-Key", options.Value.ApiKey);
        message.Headers.Add("X-Goog-FieldMask", fieldMask);
        using var response = await httpClient.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Google Routes {Mode} request returned {Status}", mode, (int)response.StatusCode);
            return (null, ProviderHttp.Classify(response.StatusCode));
        }
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct), null);
    }
}
