using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Domain.ValueObjects;

namespace TravelPlanner.Application.Planning;

/// <summary>
/// Search pipeline with bounded fan-out:
/// 1. date combinations → 2. flights per (destination, date, provider), capped by MaxFlightSearches →
/// 3. shortlist the cheapest affordable dates per destination → 4. one accommodation search per
/// unique (destination, check-in, check-out, guests) → 5. budget pre-check and preliminary ranking →
/// 6. transfers only for the finalists → 7. final budget, filter, rank, diversify.
/// </summary>
public sealed class TripPlanningEngine(
    IEnumerable<IFlightSearchProvider> flightProviders,
    IEnumerable<IAccommodationSearchProvider> accommodationProviders,
    IEnumerable<ITransportSearchProvider> transportProviders,
    IExchangeRateProvider exchangeRateProvider,
    IAccommodationSearchLinkBuilder accommodationSearchLinks,
    IOutboundLinkPolicy linkPolicy,
    IOptions<PlanningOptions> planningOptions,
    IOptions<BudgetOptions> budgetOptions,
    TimeProvider timeProvider,
    ILogger<TripPlanningEngine> logger)
{
    private readonly PlanningOptions _options = planningOptions.Value;
    private readonly BudgetOptions _budget = budgetOptions.Value;

    public async Task<PlanningResult> PlanAsync(
        PlanningRequest request, Func<PlanningProgress, Task>? progress, CancellationToken cancellationToken)
    {
        var flights = flightProviders.ToList();
        if (flights.Count == 0)
            throw new PlanningException(T.Get("error.noFlightProvider"));

        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var warnings = new WarningCollector();
        var prefs = request.Preferences;
        var currency = request.Currency.ToUpperInvariant();
        var conversion = new CurrencyConversionContext(exchangeRateProvider, currency);
        Task Report(int percent, string message) => progress?.Invoke(new PlanningProgress(percent, message)) ?? Task.CompletedTask;

        // 1. Date combinations
        var combos = DateCombinationGenerator.Generate(request.EarliestDeparture, request.LatestReturn, request.DurationDays, today, _options.MaxDateCombinations);
        if (combos.Count == 0)
            throw new PlanningException(T.Get("error.noDates"));

        var destinations = request.Destinations
            .Where(d => d.Airports.Count > 0 && d.Airports.All(a => a.IataCode != request.Origin.IataCode))
            .ToList();
        if (destinations.Count == 0)
            throw new PlanningException(T.Get("error.noDestinations"));

        // 2. Flights (bounded)
        var maxDestinations = Math.Max(1, _options.MaxFlightSearches / flights.Count);
        if (destinations.Count > maxDestinations)
        {
            warnings.Add(T.Get("warn.destinationLimit", maxDestinations, destinations.Count));
            destinations = destinations.Take(maxDestinations).ToList();
        }
        var datesPerDestination = Math.Clamp(_options.MaxFlightSearches / (destinations.Count * flights.Count), 1, combos.Count);
        var searchedDates = DateCombinationGenerator.Sample(combos, datesPerDestination);
        if (searchedDates.Count < combos.Count)
            warnings.Add(T.Get("warn.dateLimit", searchedDates.Count, combos.Count));

        await Report(5, T.Get("progress.flightsStart", destinations.Count, searchedDates.Count));

        var flightJobs = (from d in destinations from c in searchedDates from p in flights select (Destination: d, Dates: c, Provider: p)).ToList();
        var flightResults = new ConcurrentDictionary<(Guid DestinationId, TravelDates Dates), ConcurrentBag<FlightOffer>>();
        var flightDone = 0;

        await Parallel.ForEachAsync(flightJobs, ParallelismFor(cancellationToken), async (job, ct) =>
        {
            var airport = PrimaryAirport(job.Destination);
            var flightRequest = new FlightSearchRequest(request.Origin.IataCode, airport.IataCode, job.Dates.Departure, job.Dates.Return,
                request.Travelers, currency, prefs.MaxStops, prefs.IncludeCheckedBaggage, _options.MaxFlightOffersPerSearch);
            var result = await CallAsync(token => job.Provider.SearchRoundTripAsync(flightRequest, token), job.Provider.Name, ct);

            if (result.Succeeded)
            {
                var bag = flightResults.GetOrAdd((job.Destination.Id, job.Dates), _ => []);
                foreach (var offer in FlightFilter.Apply(result.Data!, prefs, now)) bag.Add(offer);
            }
            else
            {
                warnings.ProviderFailure("Flights", job.Provider.Name, result.Error);
            }

            var done = Interlocked.Increment(ref flightDone);
            await Report(5 + 45 * done / flightJobs.Count, T.Get("progress.flights", done, flightJobs.Count));
        });

        var allFlights = flightResults.Values.SelectMany(b => b).ToList();
        var stats = new PlanningStats(combos.Count, destinations.Count, flightJobs.Count, 0, 0);
        if (allFlights.Count == 0)
        {
            warnings.Add(T.Get("warn.noFlights"));
            return new PlanningResult([], false, warnings.ToList(), stats);
        }

        await conversion.EnsureAsync(allFlights.Select(f => f.Currency), cancellationToken);

        // 3. Shortlist: cheapest affordable date combinations per destination
        var shortlist = new List<(Destination Destination, TravelDates Dates, List<FlightOffer> Flights)>();
        var overBudgetFlights = new List<(Destination Destination, TravelDates Dates, FlightOffer Flight, decimal Price)>();
        foreach (var destination in destinations)
        {
            var perDate = flightResults
                .Where(kv => kv.Key.DestinationId == destination.Id)
                .Select(kv =>
                {
                    var priced = kv.Value
                        .Select(f => (Flight: f, Price: conversion.TryConvert(f.TotalPrice, f.Currency, out var p) ? p : (decimal?)null))
                        .Where(x => x.Price.HasValue)
                        .OrderBy(x => x.Price)
                        .ToList();
                    if (priced.Count < kv.Value.Count)
                        warnings.Add(T.Get("warn.flightConversion", currency));
                    return (Dates: kv.Key.Dates, Priced: priced);
                })
                .Where(x => x.Priced.Count > 0)
                .ToList();

            foreach (var entry in perDate)
            {
                var cheapest = entry.Priced[0];
                if (cheapest.Price > request.Budget)
                    overBudgetFlights.Add((destination, entry.Dates, cheapest.Flight, cheapest.Price!.Value));
            }

            shortlist.AddRange(perDate
                .Select(x => (x.Dates, Flights: x.Priced.Where(p => p.Price <= request.Budget).Select(p => p.Flight).ToList()))
                .Where(x => x.Flights.Count > 0)
                .OrderBy(x => conversion.TryConvert(x.Flights[0].TotalPrice, x.Flights[0].Currency, out var p) ? p : decimal.MaxValue)
                .Take(_options.MaxCombosPerDestinationForAccommodation)
                .Select(x => (destination, x.Dates, x.Flights)));
        }

        if (shortlist.Count == 0)
        {
            warnings.Add(T.Get("warn.flightsOverBudget"));
            var fallback = overBudgetFlights
                .OrderBy(x => x.Price)
                .DistinctBy(x => x.Destination.Id)
                .Take(_options.MaxOverBudgetFallbackPlans)
                .Select(x => BuildCandidate(x.Destination, x.Dates, x.Flight, null, T.Get("reason.flightsExceedBudget"), request, now))
                .ToList();
            foreach (var c in fallback) c.Budget = CalculateBudget(c, request, conversion, now);
            return new PlanningResult(fallback, false, warnings.ToList(), stats);
        }

        // 4. Accommodation: one search per unique (destination, dates, guests) and provider
        var accProviders = accommodationProviders.ToList();
        if (accProviders.Count == 0)
            warnings.Add(T.Get("warn.noAccommodationProvider"));

        var rooms = (int)Math.Ceiling(request.Travelers / 2.0);
        var stays = shortlist.Select(s => (s.Destination, s.Dates)).DistinctBy(s => (s.Destination.Id, s.Dates)).ToList();
        var accJobs = (from s in stays from p in accProviders select (s.Destination, s.Dates, Provider: p)).ToList();
        var accResults = new ConcurrentDictionary<(Guid DestinationId, TravelDates Dates), ConcurrentBag<AccommodationOffer>>();
        var accDone = 0;

        await Report(52, T.Get("progress.accommodationStart", accJobs.Count));
        await Parallel.ForEachAsync(accJobs, ParallelismFor(cancellationToken), async (job, ct) =>
        {
            var accRequest = new AccommodationSearchRequest(job.Destination.Id, job.Destination.CityIn(T.CurrentLanguage), job.Destination.CountryCode,
                job.Destination.BookingComCityId, job.Destination.Latitude, job.Destination.Longitude, job.Dates.CheckIn, job.Dates.CheckOut,
                request.Travelers, rooms, currency, prefs.MinAccommodationStars, _options.MaxAccommodationResults);
            var result = await CallAsync(token => job.Provider.SearchAsync(accRequest, token), job.Provider.Name, ct);

            var bag = accResults.GetOrAdd((job.Destination.Id, job.Dates), _ => []);
            if (result.Succeeded)
            {
                foreach (var offer in result.Data!.Where(o => AccommodationMatches(o, prefs, now))) bag.Add(offer);
            }
            else
            {
                warnings.ProviderFailure("Accommodation", job.Provider.Name, result.Error);
            }

            var done = Interlocked.Increment(ref accDone);
            await Report(52 + 23 * done / Math.Max(1, accJobs.Count), T.Get("progress.accommodation", done, accJobs.Count));
        });

        var allAccommodation = accResults.Values.SelectMany(b => b).ToList();
        await conversion.EnsureAsync(allAccommodation.Select(a => a.Currency).Concat(allAccommodation.SelectMany(a => a.Fees).Select(f => f.Currency)), cancellationToken);
        await conversion.EnsureAsync([_budget.EstimatesCurrency], cancellationToken);
        if (prefs.IncludeDailyExpenseEstimates && string.IsNullOrWhiteSpace(_budget.EstimatesCurrency))
            warnings.Add(T.Get("warn.dailyNotConfigured"));

        // 5. Candidates + preliminary budget/ranking (transfers not yet known)
        var candidates = new List<PlanCandidate>();
        foreach (var (destination, dates, offers) in shortlist)
        {
            var hotels = accResults.TryGetValue((destination.Id, dates), out var bag)
                ? bag.Select(h => (Offer: h, Price: conversion.TryConvert(h.TotalPrice, h.Currency, out var p) ? p : decimal.MaxValue))
                    .OrderBy(h => h.Price).ThenByDescending(h => h.Offer.ReviewScore ?? 0)
                    .DistinctBy(h => h.Offer.PropertyId)
                    .Take(_options.MaxAccommodationsPerCandidate)
                    .Select(h => h.Offer).ToList()
                : [];

            foreach (var flight in offers.Take(_options.MaxFlightsPerCandidate))
            {
                if (hotels.Count == 0)
                {
                    var reason = accProviders.Count == 0 ? T.Get("reason.noAccommodationProvider") : T.Get("reason.noAccommodationMatch");
                    candidates.Add(BuildCandidate(destination, dates, flight, null, reason, request, now));
                    continue;
                }
                candidates.AddRange(hotels.Select(h => BuildCandidate(destination, dates, flight, h, null, request, now)));
            }
        }

        foreach (var c in candidates) c.Budget = CalculateBudget(c, request, conversion, now);
        ApplyScores(candidates, request.Budget);

        var finalists = Diversify(candidates.Where(c => c.Budget.IsAffordable).OrderByDescending(c => c.Score!.Score), _options.MaxResults * 2);
        if (finalists.Count == 0)
        {
            finalists = candidates.OrderBy(c => c.Budget.TotalEstimatedCost).DistinctBy(c => c.Destination.Id)
                .Take(_options.MaxOverBudgetFallbackPlans).ToList();
        }

        // 6. Transfers only for finalists, deduplicated per (airport, accommodation location)
        var transport = transportProviders.ToList();
        var transferCache = new ConcurrentDictionary<string, IReadOnlyList<TransferOption>>();
        var transportCalls = 0;
        await Report(78, T.Get("progress.transfers"));
        await Parallel.ForEachAsync(finalists.Where(c => c.Accommodation is { HasCoordinates: true }).DistinctBy(TransferKey), ParallelismFor(cancellationToken), async (c, ct) =>
        {
            var options = new List<TransferOption>();
            var transportRequest = new TransportSearchRequest(c.Destination.Id, c.ArrivalAirport.IataCode, c.ArrivalAirport.Name,
                c.ArrivalAirport.Latitude, c.ArrivalAirport.Longitude, c.Accommodation!.Latitude!.Value, c.Accommodation.Longitude!.Value,
                c.Accommodation.Name, request.Travelers, currency);
            foreach (var provider in transport)
            {
                Interlocked.Increment(ref transportCalls);
                var result = await CallAsync(token => provider.SearchAsync(transportRequest, token), provider.Name, ct);
                if (result.Succeeded) options.AddRange(result.Data!);
                else warnings.ProviderFailure("Transfers", provider.Name, result.Error);
            }
            transferCache[TransferKey(c)] = options;
        });

        foreach (var c in finalists)
        {
            var routed = transferCache.GetValueOrDefault(TransferKey(c)) ?? [];
            var options = BuildTransferOptions(c.Destination, c.ArrivalAirport, routed);
            await conversion.EnsureAsync(options.Select(o => o.CostCurrency), cancellationToken);
            c.TransferDuration = options.Where(o => o.Duration.HasValue && o.Mode != TransferMode.Information).Select(o => o.Duration).Min();
            c.Drafts.Add(PlanItemDraftFactory.Transfer(TransferDirection.AirportToAccommodation, c.ArrivalAirport, c.Accommodation, options, request.Travelers, now));
            c.Drafts.Add(PlanItemDraftFactory.Transfer(TransferDirection.AccommodationToAirport, c.ArrivalAirport, c.Accommodation, options, request.Travelers, now));
            c.Budget = CalculateBudget(c, request, conversion, now);
        }

        // 7. Final ranking
        await Report(95, T.Get("progress.ranking"));
        ApplyScores(finalists, request.Budget);
        var affordable = finalists.Where(c => c.Budget.IsAffordable).OrderByDescending(c => c.Score!.Score).ToList();
        var resultPlans = affordable.Count > 0
            ? Diversify(affordable, _options.MaxResults)
            : finalists.OrderBy(c => c.Budget.TotalEstimatedCost).Take(_options.MaxOverBudgetFallbackPlans).ToList();

        if (affordable.Count == 0)
            warnings.Add(T.Get("warn.noPlanFits"));
        foreach (var w in resultPlans.SelectMany(p => p.Budget.Warnings)) warnings.Add(w);

        logger.LogInformation("Planning finished: {Plans} plan(s), {FlightCalls} flight / {AccCalls} accommodation / {TransportCalls} transport call(s)",
            resultPlans.Count, flightJobs.Count, accJobs.Count, transportCalls);

        return new PlanningResult(resultPlans, affordable.Count > 0, warnings.ToList(),
            stats with { AccommodationSearches = accJobs.Count, TransportSearches = transportCalls });
    }

    private PlanCandidate BuildCandidate(Destination destination, TravelDates dates, FlightOffer flight, AccommodationOffer? accommodation,
        string? unavailableReason, PlanningRequest request, DateTimeOffset now)
    {
        var arrival = destination.Airports.FirstOrDefault(a => a.IataCode == flight.Outbound.ArrivalAirport) ?? PrimaryAirport(destination);
        var rooms = (int)Math.Ceiling(request.Travelers / 2.0);
        var candidate = new PlanCandidate
        {
            Destination = destination,
            ArrivalAirport = arrival,
            Dates = dates,
            Flight = flight,
            Accommodation = accommodation
        };
        candidate.Drafts.Add(PlanItemDraftFactory.Flight(flight, request.Travelers));
        if (accommodation is not null)
            candidate.Drafts.AddRange(PlanItemDraftFactory.Accommodation(accommodation, dates, request.Travelers, rooms));
        else
            candidate.Drafts.Add(PlanItemDraftFactory.AccommodationUnavailable(dates, unavailableReason ?? string.Empty, now,
                accommodationSearchLinks.Build(destination.City, destination.Country, dates.CheckIn, dates.CheckOut, request.Travelers, rooms)));

        if (request.Preferences.IncludeDailyExpenseEstimates)
            candidate.Drafts.AddRange(PlanItemDraftFactory.DailyExpenses(_budget, dates.DurationDays, request.Travelers, now));
        return candidate;
    }

    private BudgetResult CalculateBudget(PlanCandidate candidate, PlanningRequest request, CurrencyConversionContext conversion, DateTimeOffset now) =>
        BudgetCalculator.Calculate(request.Budget, request.Currency, candidate.Drafts, conversion.Snapshot(),
            request.Preferences.IncludeDailyExpenseEstimates ? _budget.ContingencyPercent : 0m, now);

    private void ApplyScores(List<PlanCandidate> candidates, decimal budget)
    {
        var scores = PlanScorer.Score(candidates.Select(c => new ScoreInput(c.Budget.TotalEstimatedCost, budget, c.Flight, c.Accommodation,
            c.TransferDuration, c.Budget.Fit == BudgetFitStatus.WithinBudgetIncomplete)).ToList(), _options.Ranking);
        for (var i = 0; i < candidates.Count; i++) candidates[i].Score = scores[i];
    }

    private List<PlanCandidate> Diversify(IEnumerable<PlanCandidate> ordered, int take)
    {
        var perDestination = new Dictionary<Guid, int>();
        var result = new List<PlanCandidate>();
        foreach (var c in ordered)
        {
            var count = perDestination.GetValueOrDefault(c.Destination.Id);
            if (count >= _options.MaxPlansPerDestination) continue;
            perDestination[c.Destination.Id] = count + 1;
            result.Add(c);
            if (result.Count == take) break;
        }
        return result;
    }

    /// <summary>Provider route options + curated official links + optional configured taxi tariff estimate.</summary>
    private List<TransferOption> BuildTransferOptions(Destination destination, Airport airport, IReadOnlyList<TransferOption> routed)
    {
        var options = routed.Select(o => o with { Url = linkPolicy.Sanitize(o.Url), IsConfirmedQuote = false }).ToList();

        if (destination.HasTaxiTariff &&
            routed.Where(o => o.Mode == TransferMode.Drive && o.DistanceMeters.HasValue).MinBy(o => o.DistanceMeters) is { } drive)
        {
            var km = drive.DistanceMeters!.Value / 1000m;
            options.Add(new TransferOption
            {
                Mode = TransferMode.Taxi,
                Title = T.Get("transfer.taxiTariffTitle"),
                Provider = destination.TaxiTariffSource ?? T.Get("transfer.taxiTariffProvider"),
                DistanceMeters = drive.DistanceMeters,
                Duration = drive.Duration,
                EstimatedCost = Math.Round(destination.TaxiBaseFare!.Value + destination.TaxiPerKm!.Value * km, 2, MidpointRounding.AwayFromZero),
                CostCurrency = destination.TaxiTariffCurrency,
                CostVerification = VerificationStatus.Estimate,
                InfoVerification = VerificationStatus.Estimate,
                Notes = T.Get("transfer.taxiTariffNotes")
            });
        }

        if (linkPolicy.Sanitize(airport.OfficialWebsiteUrl) is { } airportUrl)
        {
            options.Add(new TransferOption
            {
                Mode = TransferMode.Information,
                Title = T.Get("transfer.airportInfoTitle", AirportName(airport)),
                Provider = AirportName(airport),
                Url = airportUrl,
                InfoVerification = VerificationStatus.Verified,
                Notes = T.Get("transfer.airportInfoNotes")
            });
        }

        foreach (var link in destination.TransportLinks)
        {
            if (linkPolicy.Sanitize(link.Url) is not { } url) continue;
            options.Add(new TransferOption
            {
                Mode = link.Kind switch
                {
                    TransportLinkKind.PublicTransport => TransferMode.PublicTransport,
                    TransportLinkKind.TaxiOperator => TransferMode.Taxi,
                    TransportLinkKind.RideHailingPlatform => TransferMode.RideHailing,
                    _ => TransferMode.Information
                },
                Title = link.NameIn(T.CurrentLanguage),
                Provider = link.NameIn(T.CurrentLanguage),
                Url = url,
                Phone = link.IsVerified ? link.Phone : null,
                Notes = link.Notes,
                InfoVerification = link.IsVerified ? VerificationStatus.Verified : VerificationStatus.Estimate
            });
        }

        return options;
    }

    private static bool AccommodationMatches(AccommodationOffer offer, TripPreferences prefs, DateTimeOffset now)
    {
        if (offer.TotalPrice <= 0) return false;
        if (offer.ExpiresAtUtc.HasValue && offer.ExpiresAtUtc <= now) return false;
        if (prefs.MinAccommodationStars is { } min && (offer.Stars is null || offer.Stars < min)) return false;
        return true;
    }

    private static string AirportName(Airport airport) => airport.NameIn(T.CurrentLanguage);

    /// <summary>Airport closest to the city centre.</summary>
    internal static Airport PrimaryAirport(Destination destination) =>
        destination.Airports.MinBy(a => Math.Pow(a.Latitude - destination.Latitude, 2) + Math.Pow(a.Longitude - destination.Longitude, 2))!;

    private static string TransferKey(PlanCandidate c) =>
        $"{c.ArrivalAirport.IataCode}|{c.Accommodation?.Provider}|{c.Accommodation?.PropertyId}";

    private ParallelOptions ParallelismFor(CancellationToken ct) => new()
    {
        MaxDegreeOfParallelism = Math.Max(1, _options.MaxConcurrentProviderCalls),
        CancellationToken = ct
    };

    private async Task<ProviderResult<T>> CallAsync<T>(
        Func<CancellationToken, Task<ProviderResult<T>>> call, string provider, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ProviderCallTimeoutSeconds)));
        try
        {
            return await call(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult<T>.Failure(provider, ProviderErrorKind.Timeout, "The provider did not respond in time.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Provider {Provider} call failed", provider);
            return ProviderResult<T>.Failure(provider, ProviderErrorKind.Unavailable, "The provider request failed.");
        }
    }
}
