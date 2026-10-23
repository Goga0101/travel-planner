using System.Globalization;
using System.Xml;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Infrastructure.Providers.Amadeus;

public sealed class AmadeusFlightOfferMapper(int offerValidityMinutes) : IFlightOfferMapper<AmadeusFlightOffersResponse>
{
    public const string ProviderName = "Amadeus";

    public IReadOnlyList<FlightOffer> Map(AmadeusFlightOffersResponse response, FlightSearchRequest request, DateTimeOffset retrievedAtUtc)
    {
        var offers = new List<FlightOffer>();
        foreach (var raw in response.Data)
        {
            if (raw.Itineraries.Count != 2 || raw.Itineraries.Any(i => i.Segments.Count == 0)) continue;
            var priceText = raw.Price?.GrandTotal ?? raw.Price?.Total;
            if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price <= 0) continue;
            if (string.IsNullOrWhiteSpace(raw.Price?.Currency)) continue;

            var outbound = MapItinerary(raw.Itineraries[0]);
            var inbound = MapItinerary(raw.Itineraries[1]);
            if (outbound is null || inbound is null) continue;

            var bags = raw.TravelerPricings?.FirstOrDefault()?.FareDetailsBySegment
                .Select(f => f.IncludedCheckedBags)
                .Select(b => b is null ? (int?)null : b.Quantity ?? (b.Weight > 0 ? 1 : 0))
                .ToList();
            int? includedBags = bags is { Count: > 0 } && bags.All(b => b.HasValue) ? bags.Min() : null;

            offers.Add(new FlightOffer
            {
                Provider = ProviderName,
                Signature = Signature(raw.Itineraries),
                TotalPrice = price,
                Currency = raw.Price!.Currency!.ToUpperInvariant(),
                Outbound = outbound,
                Inbound = inbound,
                IncludedCheckedBags = includedBags,
                BaggageNote = includedBags is null ? T.Get("note.baggageUnknown") : null,
                ValidatingCarrier = raw.ValidatingAirlineCodes?.FirstOrDefault(),
                BookingUrl = null,
                SourceNote = T.Get("note.amadeus"),
                Verification = VerificationStatus.Live,
                RetrievedAtUtc = retrievedAtUtc,
                ExpiresAtUtc = retrievedAtUtc.AddMinutes(offerValidityMinutes)
            });
        }
        return offers;
    }

    private static FlightItinerary? MapItinerary(AmadeusItinerary itinerary)
    {
        var segments = new List<FlightSegment>();
        foreach (var s in itinerary.Segments)
        {
            if (!TryLocal(s.Departure.At, out var dep)) return null;
            DateTime? arr = TryLocal(s.Arrival.At, out var a) ? a : null;
            segments.Add(new FlightSegment(s.CarrierCode, $"{s.CarrierCode}{s.Number}", s.Departure.IataCode, s.Arrival.IataCode, dep, arr, ParseDuration(s.Duration)));
        }

        var stops = itinerary.Segments.Count - 1 + itinerary.Segments.Sum(s => s.NumberOfStops);
        return new FlightItinerary(segments[0].DepartureAirport, segments[^1].ArrivalAirport, segments[0].DepartureLocal,
            segments[^1].ArrivalLocal, ParseDuration(itinerary.Duration), stops, segments);
    }

    private static string Signature(IEnumerable<AmadeusItinerary> itineraries) =>
        string.Join("|", itineraries.SelectMany(i => i.Segments).Select(s => $"{s.CarrierCode}{s.Number}@{s.Departure.At}"));

    private static bool TryLocal(string value, out DateTime result) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>ISO-8601 duration such as "PT2H35M".</summary>
    public static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return XmlConvert.ToTimeSpan(value); }
        catch (FormatException) { return null; }
    }
}
