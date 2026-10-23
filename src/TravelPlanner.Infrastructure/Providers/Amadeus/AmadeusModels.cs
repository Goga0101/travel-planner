using System.Text.Json.Serialization;

namespace TravelPlanner.Infrastructure.Providers.Amadeus;

public sealed class AmadeusOptions
{
    public const string SectionName = "Providers:Amadeus";

    public bool Enabled { get; set; }
    /// <summary>
    /// Amadeus Self-Service (test.api.amadeus.com / api.amadeus.com self-service keys) was decommissioned on 2026-07-17.
    /// Set this to the base URL provided with your Amadeus Enterprise contract.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.amadeus.com";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    /// <summary>How long a priced offer is presented as current before it is marked expired.</summary>
    public int OfferValidityMinutes { get; set; } = 30;

    public bool IsConfigured => Enabled && ProviderHttp.HasValue(ClientId) && ProviderHttp.HasValue(ClientSecret) && ProviderHttp.HasValue(BaseUrl);
}

// Subset of the Flight Offers Search v2 response used by the mapper.

public sealed class AmadeusFlightOffersResponse
{
    [JsonPropertyName("data")] public List<AmadeusFlightOffer> Data { get; set; } = [];
}

public sealed class AmadeusFlightOffer
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("itineraries")] public List<AmadeusItinerary> Itineraries { get; set; } = [];
    [JsonPropertyName("price")] public AmadeusPrice? Price { get; set; }
    [JsonPropertyName("validatingAirlineCodes")] public List<string>? ValidatingAirlineCodes { get; set; }
    [JsonPropertyName("travelerPricings")] public List<AmadeusTravelerPricing>? TravelerPricings { get; set; }
}

public sealed class AmadeusItinerary
{
    [JsonPropertyName("duration")] public string? Duration { get; set; }
    [JsonPropertyName("segments")] public List<AmadeusSegment> Segments { get; set; } = [];
}

public sealed class AmadeusSegment
{
    [JsonPropertyName("departure")] public AmadeusEndpoint Departure { get; set; } = new();
    [JsonPropertyName("arrival")] public AmadeusEndpoint Arrival { get; set; } = new();
    [JsonPropertyName("carrierCode")] public string CarrierCode { get; set; } = "";
    [JsonPropertyName("number")] public string Number { get; set; } = "";
    [JsonPropertyName("duration")] public string? Duration { get; set; }
    [JsonPropertyName("numberOfStops")] public int NumberOfStops { get; set; }
}

public sealed class AmadeusEndpoint
{
    [JsonPropertyName("iataCode")] public string IataCode { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = "";
}

public sealed class AmadeusPrice
{
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("total")] public string? Total { get; set; }
    [JsonPropertyName("grandTotal")] public string? GrandTotal { get; set; }
}

public sealed class AmadeusTravelerPricing
{
    [JsonPropertyName("fareDetailsBySegment")] public List<AmadeusFareDetails> FareDetailsBySegment { get; set; } = [];
}

public sealed class AmadeusFareDetails
{
    [JsonPropertyName("includedCheckedBags")] public AmadeusCheckedBags? IncludedCheckedBags { get; set; }
}

public sealed class AmadeusCheckedBags
{
    [JsonPropertyName("quantity")] public int? Quantity { get; set; }
    [JsonPropertyName("weight")] public int? Weight { get; set; }
    [JsonPropertyName("weightUnit")] public string? WeightUnit { get; set; }
}

public sealed class AmadeusTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}
