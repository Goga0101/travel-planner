using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Application.Planning;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Domain.ValueObjects;

namespace TravelPlanner.Application.Features.TripPlans;

public sealed record RefreshTripPlanPricesCommand(Guid Id) : IRequest<TripPlanDetailDto>;

/// <summary>
/// Re-queries the original providers for the plan's flight and accommodation, matches the same offer
/// (flight itinerary signature / property id) and recalculates the budget. Offers that disappeared are
/// replaced by the cheapest equivalent from the same provider or marked unavailable — never silently kept as current.
/// </summary>
public sealed class RefreshTripPlanPricesHandler(
    IApplicationDbContext db,
    IEnumerable<IFlightSearchProvider> flightProviders,
    IEnumerable<IAccommodationSearchProvider> accommodationProviders,
    IExchangeRateProvider exchangeRateProvider,
    IOutboundLinkPolicy linkPolicy,
    IOptions<PlanningOptions> planningOptions,
    IOptions<BudgetOptions> budgetOptions,
    TimeProvider timeProvider) : IRequestHandler<RefreshTripPlanPricesCommand, TripPlanDetailDto>
{
    public async Task<TripPlanDetailDto> Handle(RefreshTripPlanPricesCommand request, CancellationToken cancellationToken)
    {
        var plan = await TripPlanLoader.LoadAsync(db, request.Id, tracking: true, cancellationToken)
                   ?? throw new NotFoundException("resource.tripPlan", request.Id);
        var search = plan.TripSearch!;
        // Regenerated item texts keep the language the search was created in; the response DTO uses the request language.
        var requestLanguage = T.CurrentLanguage;
        var searchLanguage = T.Use(search.Language);
        var destination = plan.Destination!;
        var now = timeProvider.GetUtcNow();
        var dates = new TravelDates(plan.DepartureDate, plan.ReturnDate);
        var warnings = new List<string>();
        var drafts = plan.Items.OrderBy(i => i.SortOrder).Where(i => i.Type != PlanItemType.Contingency).Select(PlanEntityFactory.ToDraft).ToList();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, planningOptions.Value.ProviderCallTimeoutSeconds * 2)));
        var ct = timeout.Token;

        // Flight
        var flightIndex = drafts.FindIndex(d => d.Type == PlanItemType.Flight);
        if (flightIndex >= 0)
        {
            var old = drafts[flightIndex];
            var details = Deserialize<FlightItemDetails>(old.DetailsJson);
            var provider = flightProviders.FirstOrDefault(p => p.Name == old.Provider);
            if (provider is null || details is null)
            {
                warnings.Add(T.Get("refresh.flightProviderMissing", old.Provider));
            }
            else
            {
                var result = await provider.SearchRoundTripAsync(new FlightSearchRequest(details.Outbound.DepartureAirport, details.Outbound.ArrivalAirport,
                    dates.Departure, dates.Return, plan.Travelers, plan.Currency, search.Preferences.MaxStops,
                    search.Preferences.IncludeCheckedBaggage, 20), ct);

                if (!result.Succeeded)
                {
                    warnings.Add(T.Get("refresh.flightFailed", result.Error));
                }
                else if (result.Data!.FirstOrDefault(o => o.Signature == old.ProviderOfferId) is { } same)
                {
                    drafts[flightIndex] = PlanItemDraftFactory.Flight(same, plan.Travelers);
                }
                else if (FlightFilter.Apply(result.Data!, search.Preferences, now).MinBy(o => o.TotalPrice) is { } alternative)
                {
                    drafts[flightIndex] = PlanItemDraftFactory.Flight(alternative, plan.Travelers);
                    warnings.Add(T.Get("refresh.flightReplaced", provider.Name));
                }
                else
                {
                    drafts[flightIndex] = old with
                    {
                        Amount = null, Verification = VerificationStatus.Unavailable, BookingUrl = null, RetrievedAtUtc = now,
                        Summary = T.Get("refresh.flightGoneSummary")
                    };
                    warnings.Add(T.Get("refresh.flightGone"));
                }
            }
        }

        // Accommodation (+ its fees)
        var accIndex = drafts.FindIndex(d => d.Type == PlanItemType.Accommodation);
        if (accIndex >= 0 && drafts[accIndex].Verification != VerificationStatus.Unavailable)
        {
            var old = drafts[accIndex];
            var provider = accommodationProviders.FirstOrDefault(p => p.Name == old.Provider);
            if (provider is null)
            {
                warnings.Add(T.Get("refresh.stayProviderMissing", old.Provider));
            }
            else
            {
                var rooms = (int)Math.Ceiling(plan.Travelers / 2.0);
                var result = await provider.SearchAsync(new AccommodationSearchRequest(destination.Id, destination.CityIn(T.CurrentLanguage), destination.CountryCode,
                    destination.BookingComCityId, destination.Latitude, destination.Longitude, dates.CheckIn, dates.CheckOut, plan.Travelers, rooms,
                    plan.Currency, search.Preferences.MinAccommodationStars, planningOptions.Value.MaxAccommodationResults), ct);

                if (!result.Succeeded)
                {
                    warnings.Add(T.Get("refresh.stayFailed", result.Error));
                }
                else
                {
                    drafts.RemoveAll(d => d.Type == PlanItemType.Fee);
                    accIndex = drafts.FindIndex(d => d.Type == PlanItemType.Accommodation);
                    if (result.Data!.FirstOrDefault(o => o.PropertyId == old.ProviderOfferId) is { } same)
                    {
                        drafts.RemoveAt(accIndex);
                        drafts.InsertRange(accIndex, PlanItemDraftFactory.Accommodation(same, dates, plan.Travelers, rooms));
                    }
                    else
                    {
                        drafts[accIndex] = old with
                        {
                            Amount = null, Verification = VerificationStatus.Unavailable, BookingUrl = null, RetrievedAtUtc = now,
                            Summary = T.Get("refresh.stayGoneSummary")
                        };
                        warnings.Add(T.Get("refresh.stayGone"));
                    }
                }
            }
        }

        var conversion = new CurrencyConversionContext(exchangeRateProvider, plan.Currency);
        await conversion.EnsureAsync(drafts.Select(d => d.Currency), ct);
        var contingency = search.Preferences.IncludeDailyExpenseEstimates ? budgetOptions.Value.ContingencyPercent : 0m;
        var budget = BudgetCalculator.Calculate(plan.BudgetAmount, plan.Currency, drafts, conversion.Snapshot(), contingency, now);

        PlanEntityFactory.ApplyBudget(plan, budget, linkPolicy);
        plan.Warnings.AddRange(warnings.Where(w => !plan.Warnings.Contains(w)));
        plan.PricesRefreshedAtUtc = now;
        searchLanguage.Dispose();
        await db.SaveChangesAsync(cancellationToken);

        using var _ = T.Use(requestLanguage);
        return plan.ToDetail(now, linkPolicy);
    }

    private static T? Deserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, AppJson.Options); }
        catch (JsonException) { return null; }
    }
}
