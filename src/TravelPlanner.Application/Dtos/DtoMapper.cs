using System.Text.Json;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Planning;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Dtos;

public static class DtoMapper
{
    // Catalog names are returned in the request language (CurrentUICulture), falling back to English.
    private static string Lang => T.CurrentLanguage;

    public static AirportDto ToDto(this Airport a) =>
        new(a.Id, a.IataCode, a.NameIn(Lang), a.CityIn(Lang), a.CountryCode, a.Latitude, a.Longitude,
            a.IsDepartureOption, a.OfficialWebsiteUrl, a.CityCode, a.Country, a.Continent);

    public static DestinationRefDto ToRef(this Destination d) => new(d.Id, d.Code, d.CityIn(Lang), d.CountryIn(Lang), d.CountryCode);

    public static DestinationSummaryDto ToSummary(this Destination d) =>
        new(d.Id, d.Code, d.CityIn(Lang), d.CountryIn(Lang), d.CountryCode, d.Region, d.Airports.Select(a => a.IataCode).Order().ToList(), d.Popularity);

    public static DestinationDetailDto ToDetail(this Destination d, IOutboundLinkPolicy linkPolicy) =>
        new(d.Id, d.Code, d.CityIn(Lang), d.CountryIn(Lang), d.CountryCode, d.Region, d.DescriptionIn(Lang), d.Latitude, d.Longitude, d.IsActive,
            d.Popularity, d.HasTaxiTariff,
            d.Airports.OrderBy(a => a.IataCode).Select(a => a.ToDto() with { OfficialWebsiteUrl = linkPolicy.Sanitize(a.OfficialWebsiteUrl) }).ToList(),
            d.TransportLinks.OrderBy(l => l.Kind).ThenBy(l => l.Name)
                .Select(l => new TransportLinkDto(l.Id, l.Kind, l.NameIn(Lang), linkPolicy.Sanitize(l.Url), l.IsVerified ? l.Phone : null, l.Notes,
                    l.IsVerified, l.VerifiedOn))
                .ToList());

    public static TripSearchDto ToDto(this TripSearch s, IReadOnlyList<Destination> selected, int planCount) =>
        new(s.Id, s.Status, s.IsFinished, s.ProgressPercent, s.ProgressMessage, s.ErrorMessage, s.Warnings, s.DataMode,
            new TripSearchCriteriaDto(s.OriginAirportIata, s.EarliestDeparture, s.LatestReturn, s.DurationDays, s.Nights, s.Travelers,
                s.BudgetAmount, s.BudgetCurrency, s.DestinationMode, selected.Select(d => d.ToRef()).ToList(),
                new TripPreferencesDto(s.Preferences.IncludeCheckedBaggage, s.Preferences.MinAccommodationStars, s.Preferences.MaxFlightDurationHours,
                    s.Preferences.MaxStops, s.Preferences.PreferredDepartureTime, s.Preferences.IncludeDailyExpenseEstimates,
                    s.Preferences.AllowSelfTransfer),
                s.Continents),
            planCount, s.CreatedAtUtc, s.StartedAtUtc, s.CompletedAtUtc);

    public static TripPlanSummaryDto ToSummary(this TripPlan p, DateTimeOffset nowUtc, AirportLookup? airports = null)
    {
        var items = p.Items.OrderBy(i => i.SortOrder).ToList();
        var flightItem = items.FirstOrDefault(i => i.Type == PlanItemType.Flight);
        var accItem = items.FirstOrDefault(i => i.Type == PlanItemType.Accommodation);
        var transferItem = items.FirstOrDefault(i => i.Type == PlanItemType.Transfer && i.Direction == TransferDirection.AirportToAccommodation);

        return new TripPlanSummaryDto(
            p.Id, p.TripSearchId, p.Rank, p.Score, ParseBreakdown(p.ScoreBreakdownJson), p.Destination!.ToRef(),
            p.DepartureDate, p.ReturnDate, p.Nights, p.Travelers, p.Currency, p.BudgetAmount, p.TotalEstimatedCost, p.RemainingBudget,
            p.BudgetFit, p.MissingCostCategories, p.Warnings, p.IsDemo,
            items.Any(i => i.IsExpired(nowUtc)),
            items.Where(i => i.ExpiresAtUtc.HasValue).Select(i => i.ExpiresAtUtc).Min(),
            p.PricesRefreshedAtUtc,
            flightItem is null ? null : FlightSummary(flightItem, p.Currency, airports ?? AirportLookup.Empty),
            accItem is null ? null : AccommodationSummary(accItem, p.Currency),
            transferItem is null ? null : TransferSummary(transferItem, p.Currency),
            items.Where(i => i.Type is PlanItemType.Flight or PlanItemType.Accommodation or PlanItemType.Transfer)
                .Select(i => new SourceVerificationDto(i.Type, i.Direction, ProviderLabel(i.ProviderName), i.Verification, i.RetrievedAtUtc, i.ExpiresAtUtc, i.IsExpired(nowUtc)))
                .ToList());
    }

    public static TripPlanDetailDto ToDetail(this TripPlan p, DateTimeOffset nowUtc, IOutboundLinkPolicy linkPolicy, AirportLookup? airports = null)
    {
        var items = p.Items.OrderBy(i => i.SortOrder).ToList();
        var breakdown = items.Where(i => i.IncludedInTotal && i.ConvertedPrice.HasValue)
            .GroupBy(i => i.Category)
            .Select(g => new CostBreakdownLineDto(g.Key, g.Sum(i => i.ConvertedPrice!.Value)))
            .ToList();

        return new TripPlanDetailDto(
            p.ToSummary(nowUtc, airports),
            p.Destination!.ToDetail(linkPolicy),
            breakdown,
            items.Select(i => new PlanItemDto(i.Id, i.Type, i.Direction, i.Category, i.Title, i.Summary, ProviderLabel(i.ProviderName), i.Price, i.Currency,
                i.ConvertedPrice, i.ExchangeRate, i.ExchangeRateSource, i.ExchangeRateAsOfUtc, i.IncludedInTotal, i.IsRequired,
                linkPolicy.Sanitize(i.BookingUrl), i.RetrievedAtUtc, i.ExpiresAtUtc, i.IsExpired(nowUtc), i.Verification,
                AppJson.ParseElement(i.DetailsJson))).ToList());
    }

    private static FlightSummaryDto FlightSummary(TripPlanItem item, string planCurrency, AirportLookup airports)
    {
        var details = Deserialize<FlightItemDetails>(item.DetailsJson);
        return new FlightSummaryDto(ProviderLabel(item.ProviderName), item.ConvertedPrice ?? item.Price, CurrencyOf(item, planCurrency),
            item.Verification, details is null ? null : Leg(details.Outbound, details.ValidatingCarrier, airports),
            details is null ? null : Leg(details.Inbound, details.ValidatingCarrier, airports),
            details?.IncludedCheckedBags, item.BookingUrl is not null, details?.Tickets is { Count: > 0 } t && FlightTicket.IsSelfTransfer(t));
    }

    /// <summary>Carriers come from the segments; providers without segment data (Travelpayouts connections) fall back to the validating carrier.</summary>
    public static FlightLegSummaryDto Leg(FlightItinerary it, string? validatingCarrier, AirportLookup airports)
    {
        IReadOnlyList<string> carriers = it.Segments.Count > 0
            ? it.Segments.Select(s => s.CarrierCode).Distinct().ToList()
            : string.IsNullOrWhiteSpace(validatingCarrier) ? [] : [validatingCarrier];
        return new(it.DepartureAirport, it.ArrivalAirport, it.DepartureLocal, it.ArrivalLocal, it.Duration is { } d ? (int)d.TotalMinutes : null,
            it.Stops, carriers, airports.CityOf(it.DepartureAirport), airports.CityOf(it.ArrivalAirport), it.ViaAirports.Select(airports.Stop).ToList());
    }

    public static FoundFlightDto ToDto(this FoundFlight f, Destination destination, AirportLookup airports) =>
        new(destination.ToRef(), f.DepartureDate, f.ReturnDate, f.Price, f.Currency, f.OriginalPrice, f.OriginalCurrency, f.WithinBudget,
            ProviderLabel(f.Provider), f.Verification, Leg(f.Outbound, f.Carrier, airports), Leg(f.Inbound, f.Carrier, airports), f.BookingUrl,
            f.RetrievedAtUtc, f.Tickets is { Count: > 0 } t && FlightTicket.IsSelfTransfer(t), f.Tickets ?? []);

    private static AccommodationSummaryDto AccommodationSummary(TripPlanItem item, string planCurrency)
    {
        var details = Deserialize<AccommodationItemDetails>(item.DetailsJson);
        return new AccommodationSummaryDto(item.Title, ProviderLabel(item.ProviderName), item.ConvertedPrice ?? item.Price, CurrencyOf(item, planCurrency), details?.Stars,
            details?.ReviewScore, details?.Address, item.Verification, item.BookingUrl is not null, details?.CancellationPolicy);
    }

    private static TransferSummaryDto TransferSummary(TripPlanItem item, string planCurrency)
    {
        var details = Deserialize<TransferItemDetails>(item.DetailsJson);
        var selected = details?.Options.FirstOrDefault(o => o.Title == details.SelectedOptionTitle);
        var fastest = details?.Options.Where(o => o.Duration.HasValue && o.Mode != TransferMode.Information).MinBy(o => o.Duration);
        var duration = (selected ?? fastest)?.Duration;
        return new TransferSummaryDto(details?.SelectedOptionTitle ?? fastest?.Title, duration is { } d ? (int)d.TotalMinutes : null,
            item.ConvertedPrice ?? item.Price, CurrencyOf(item, planCurrency), item.Verification);
    }

    /// <summary>The demo provider name is a stable identifier (used for re-pricing); only its display label is localized.</summary>
    private static string ProviderLabel(string provider) =>
        provider == DemoProviderName ? T.Get("status.demoProvider") : provider;

    public const string DemoProviderName = "Demo data (not real)";

    private static string? CurrencyOf(TripPlanItem item, string planCurrency) =>
        item.ConvertedPrice.HasValue ? planCurrency : item.Currency;

    private static T? Deserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, AppJson.Options); }
        catch (JsonException) { return null; }
    }

    private static IReadOnlyDictionary<string, decimal> ParseBreakdown(string json) =>
        Deserialize<Dictionary<string, decimal>>(json) ?? new Dictionary<string, decimal>();
}
