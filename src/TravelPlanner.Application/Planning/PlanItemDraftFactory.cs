using System.Globalization;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Domain.ValueObjects;

namespace TravelPlanner.Application.Planning;

public static class PlanItemDraftFactory
{
    public static string TransferDisclaimer => T.Get("transfer.disclaimer");

    public static PlanItemDraft Flight(FlightOffer offer, int passengers)
    {
        var o = offer.Outbound;
        var i = offer.Inbound;
        return new PlanItemDraft
        {
            Type = PlanItemType.Flight,
            Category = CostCategories.Flights,
            Title = T.Get("flight.title", o.DepartureAirport, o.ArrivalAirport),
            Summary = T.Get("flight.summary", o.DepartureLocal, T.Stops(o.Stops), i.DepartureLocal, T.Stops(i.Stops)),
            Provider = offer.Provider,
            ProviderOfferId = offer.Signature,
            Amount = offer.TotalPrice,
            Currency = offer.Currency,
            IsRequired = true,
            BookingUrl = offer.BookingUrl,
            RetrievedAtUtc = offer.RetrievedAtUtc,
            ExpiresAtUtc = offer.ExpiresAtUtc,
            Verification = offer.Verification,
            DetailsJson = AppJson.Serialize(new FlightItemDetails(o, i, passengers, offer.IncludedCheckedBags, offer.BaggageNote,
                offer.PriceExtrapolatedFromSinglePassenger, offer.ValidatingCarrier, offer.SourceNote, offer.Tickets.Count > 0 ? offer.Tickets : null))
        };
    }

    /// <summary>
    /// Accommodation line plus one line per mandatory fee. Fees flagged as included in the provider price are
    /// emitted as informational lines (IncludedInTotal = false) so they are shown but never counted twice.
    /// </summary>
    public static IReadOnlyList<PlanItemDraft> Accommodation(AccommodationOffer offer, TravelDates dates, int guests, int rooms)
    {
        var drafts = new List<PlanItemDraft>
        {
            new()
            {
                Type = PlanItemType.Accommodation,
                Category = CostCategories.Accommodation,
                Title = offer.Name,
                Summary = T.Get("stay.summary", dates.Nights, dates.CheckIn.ToDateTime(TimeOnly.MinValue), dates.CheckOut.ToDateTime(TimeOnly.MinValue)) +
                          (offer.Stars.HasValue ? $", {offer.Stars:0.#}★" : string.Empty),
                Provider = offer.Provider,
                ProviderOfferId = offer.PropertyId,
                Amount = offer.TotalPrice,
                Currency = offer.Currency,
                IsRequired = true,
                BookingUrl = offer.BookingUrl,
                RetrievedAtUtc = offer.RetrievedAtUtc,
                ExpiresAtUtc = offer.ExpiresAtUtc,
                Verification = offer.Verification,
                DetailsJson = AppJson.Serialize(new AccommodationItemDetails(offer.Name, offer.Address, offer.Latitude, offer.Longitude,
                    offer.Stars, offer.ReviewScore, offer.Category, dates.CheckIn, dates.CheckOut, dates.Nights, guests, rooms,
                    offer.CancellationPolicy, offer.FreeCancellationUntil, offer.MealPlan, offer.Fees))
            }
        };

        foreach (var fee in offer.Fees)
        {
            drafts.Add(new PlanItemDraft
            {
                Type = PlanItemType.Fee,
                Category = CostCategories.TaxesAndFees,
                Title = fee.IncludedInPrice ? T.Get("stay.feeIncluded", fee.Name) : fee.Name,
                Summary = fee.PayableAtProperty ? T.Get("stay.payableAtProperty") : null,
                Provider = offer.Provider,
                Amount = fee.Amount,
                Currency = fee.Currency,
                IncludedInTotal = !fee.IncludedInPrice,
                IsRequired = !fee.IncludedInPrice,
                RetrievedAtUtc = offer.RetrievedAtUtc,
                ExpiresAtUtc = offer.ExpiresAtUtc,
                Verification = offer.Verification,
                DetailsJson = AppJson.Serialize(new FeeItemDetails(fee.Name, fee.IncludedInPrice, fee.PayableAtProperty, offer.Name))
            });
        }

        return drafts;
    }

    /// <summary>
    /// Placeholder line when no accommodation price could be retrieved; keeps the plan honest (incomplete).
    /// With a search link the user can look up stays on the provider's site; the cost stays unknown.
    /// </summary>
    public static PlanItemDraft AccommodationUnavailable(TravelDates dates, string reason, DateTimeOffset nowUtc,
        AccommodationSearchLink? searchLink = null) => new()
    {
        Type = PlanItemType.Accommodation,
        Category = CostCategories.Accommodation,
        Title = searchLink is null ? T.Get("stay.unavailableTitle") : T.Get("stay.searchLinkTitle", searchLink.Provider),
        Summary = searchLink is null
            ? T.Get("stay.unavailableSummary", dates.Nights, reason)
            : T.Get("stay.searchLinkSummary", dates.Nights, reason, searchLink.Provider),
        Provider = searchLink?.Provider ?? "n/a",
        IsRequired = true,
        BookingUrl = searchLink?.Url,
        RetrievedAtUtc = nowUtc,
        Verification = VerificationStatus.Unavailable
    };

    public static PlanItemDraft Transfer(
        TransferDirection direction, Airport airport, AccommodationOffer? accommodation, IReadOnlyList<TransferOption> options,
        int travelers, DateTimeOffset nowUtc)
    {
        var selected = options
            .Where(o => o.CostForTravelers(travelers).HasValue && o.CostCurrency is not null)
            .OrderBy(o => o.CostForTravelers(travelers))
            .FirstOrDefault();

        var fastest = options.Where(o => o.Duration.HasValue).OrderBy(o => o.Duration).FirstOrDefault();
        var mapsUrl = accommodation is { HasCoordinates: true }
            ? MapsDirectionsUrl(direction, airport, accommodation.Latitude!.Value, accommodation.Longitude!.Value)
            : null;

        var label = T.Get(direction == TransferDirection.AirportToAccommodation ? "transfer.toAccommodation" : "transfer.toAirport");
        string summary;
        if (selected is not null)
            summary = T.Get("transfer.summaryCost", selected.Title, selected.CostForTravelers(travelers), selected.CostCurrency) +
                      (selected.Duration.HasValue ? T.Get("transfer.summaryMinutes", (int)selected.Duration.Value.TotalMinutes) : string.Empty);
        else if (fastest is not null)
            summary = T.Get("transfer.summaryNoCost", fastest.Title, (int)fastest.Duration!.Value.TotalMinutes);
        else
            summary = T.Get(options.Count > 0 ? "transfer.seeOfficial" : "transfer.none");

        return new PlanItemDraft
        {
            Type = PlanItemType.Transfer,
            Direction = direction,
            Category = CostCategories.AirportTransfers,
            Title = T.Get("transfer.title", label, airport.IataCode),
            Summary = summary,
            Provider = selected?.Provider ?? fastest?.Provider ?? options.FirstOrDefault()?.Provider ?? "n/a",
            Amount = selected?.CostForTravelers(travelers),
            Currency = selected?.CostCurrency,
            IsRequired = true,
            BookingUrl = selected?.Url,
            RetrievedAtUtc = nowUtc,
            Verification = selected?.CostVerification ?? VerificationStatus.Unavailable,
            DetailsJson = AppJson.Serialize(new TransferItemDetails(airport.IataCode, airport.NameIn(T.CurrentLanguage), airport.Latitude, airport.Longitude,
                accommodation?.Latitude, accommodation?.Longitude, selected?.Title, mapsUrl, options, TransferDisclaimer))
        };
    }

    public static IReadOnlyList<PlanItemDraft> DailyExpenses(BudgetOptions options, int days, int travelers, DateTimeOffset nowUtc)
    {
        var drafts = new List<PlanItemDraft>();
        if (string.IsNullOrWhiteSpace(options.EstimatesCurrency)) return drafts;

        void Add(string category, string title, decimal? perDay)
        {
            if (perDay is not > 0) return;
            drafts.Add(new PlanItemDraft
            {
                Type = PlanItemType.DailyExpenseEstimate,
                Category = category,
                Title = title,
                Summary = T.Get("estimate.dailySummary", perDay, options.EstimatesCurrency, days, travelers),
                Provider = T.Get("estimate.provider"),
                Amount = perDay.Value * days * travelers,
                Currency = options.EstimatesCurrency,
                RetrievedAtUtc = nowUtc,
                Verification = VerificationStatus.Estimate,
                DetailsJson = AppJson.Serialize(new EstimateItemDetails(T.Get("estimate.dailyBasis"), perDay, days, travelers, null))
            });
        }

        Add(CostCategories.Food, T.Get("estimate.food"), options.DailyFoodPerPerson);
        Add(CostCategories.LocalTransport, T.Get("estimate.localTransport"), options.DailyLocalTransportPerPerson);
        return drafts;
    }

    /// <summary>Official Google Maps URL (no API key needed). Built from coordinates only, never from user input.</summary>
    public static string MapsDirectionsUrl(TransferDirection direction, Airport airport, double lat, double lng)
    {
        string P(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        var airportPoint = $"{P(airport.Latitude)},{P(airport.Longitude)}";
        var hotelPoint = $"{P(lat)},{P(lng)}";
        var (from, to) = direction == TransferDirection.AccommodationToAirport ? (hotelPoint, airportPoint) : (airportPoint, hotelPoint);
        return $"https://www.google.com/maps/dir/?api=1&origin={Uri.EscapeDataString(from)}&destination={Uri.EscapeDataString(to)}&travelmode=transit";
    }

}
