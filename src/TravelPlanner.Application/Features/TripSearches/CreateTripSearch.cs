using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Domain.ValueObjects;

namespace TravelPlanner.Application.Features.TripSearches;

public sealed record CreateTripSearchCommand(
    string OriginAirportIata,
    DateOnly EarliestDeparture,
    DateOnly LatestReturn,
    int DurationDays,
    int Travelers,
    decimal BudgetAmount,
    string BudgetCurrency,
    DestinationMode DestinationMode,
    IReadOnlyList<Guid>? DestinationIds,
    TripPreferencesDto? Preferences,
    IReadOnlyList<string>? Continents = null,
    IReadOnlyList<string>? DestinationCityCodes = null) : IRequest<CreateTripSearchResponse>;

public sealed class CreateTripSearchValidator : AbstractValidator<CreateTripSearchCommand>
{
    public const int MaxSearchWindowDays = 60;
    public const int MaxDaysAhead = 365;
    public const int MaxManualDestinations = 10;

    private static int ManualCount(CreateTripSearchCommand x) =>
        (x.DestinationIds?.Distinct().Count() ?? 0) + (x.DestinationCityCodes?.Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0);

    public CreateTripSearchValidator(IApplicationDbContext db, IWorldPlaces world, TimeProvider timeProvider)
    {
        DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Messages are lambdas so they are resolved in the request's language at validation time.
        RuleFor(x => x.OriginAirportIata)
            .NotEmpty().WithMessage(_ => T.Get("val.originRequired"))
            .Matches("^[A-Za-z]{3}$").WithMessage(_ => T.Get("val.originFormat"))
            .MustAsync(async (code, ct) =>
            {
                var upper = code.ToUpperInvariant();
                return await db.Airports.AnyAsync(a => a.IataCode == upper && a.IsDepartureOption, ct);
            })
            .When(x => x.OriginAirportIata is { Length: 3 }, ApplyConditionTo.CurrentValidator)
            .WithMessage(_ => T.Get("val.originUnsupported"));

        RuleFor(x => x.DurationDays)
            .InclusiveBetween(TravelDates.MinimumDurationDays, TravelDates.MaximumDurationDays)
            .WithMessage(_ => T.Get("val.duration", TravelDates.MinimumDurationDays, TravelDates.MaximumDurationDays));

        RuleFor(x => x.EarliestDeparture)
            .Must(d => d > Today()).WithMessage(_ => T.Get("val.departureFuture"))
            .Must(d => d <= Today().AddDays(MaxDaysAhead)).WithMessage(_ => T.Get("val.departureWithin", MaxDaysAhead));

        RuleFor(x => x.LatestReturn)
            .GreaterThan(x => x.EarliestDeparture).WithMessage(_ => T.Get("val.returnAfter"))
            .Must((x, r) => r.DayNumber - x.EarliestDeparture.DayNumber <= MaxSearchWindowDays)
            .WithMessage(_ => T.Get("val.windowMax", MaxSearchWindowDays))
            .Must((x, r) => r.DayNumber - x.EarliestDeparture.DayNumber + 1 >= x.DurationDays)
            .WithMessage(_ => T.Get("val.windowShort"));

        RuleFor(x => x.Travelers).InclusiveBetween(1, 9).WithMessage(_ => T.Get("val.travelers", 1, 9));

        RuleFor(x => x.BudgetAmount)
            .GreaterThan(0).WithMessage(_ => T.Get("val.budgetPositive"))
            .LessThanOrEqualTo(1_000_000).WithMessage(_ => T.Get("val.budgetMax", 1_000_000))
            .PrecisionScale(12, 2, ignoreTrailingZeros: true).WithMessage(_ => T.Get("val.budgetPrecision"));

        RuleFor(x => x.BudgetCurrency)
            .NotEmpty().WithMessage(_ => T.Get("val.currencyRequired"))
            .Must(c => BudgetOptions.SupportedCurrencies.Contains(c?.ToUpperInvariant()))
            .WithMessage(_ => T.Get("val.currency", string.Join(", ", BudgetOptions.SupportedCurrencies)));

        RuleFor(x => x.DestinationMode).IsInEnum().WithMessage(_ => T.Get("val.mode"));

        // Manual mode: catalog destinations (ids) and/or any cities of the world list (IATA city or airport codes).
        When(x => x.DestinationMode == DestinationMode.Manual, () =>
        {
            RuleFor(x => x.DestinationIds)
                .Must((x, _) => ManualCount(x) > 0).WithMessage(_ => T.Get("val.manualRequired"))
                .Must((x, _) => ManualCount(x) <= MaxManualDestinations).WithMessage(_ => T.Get("val.manualMax", MaxManualDestinations))
                .MustAsync(async (ids, ct) =>
                {
                    var distinct = ids!.Distinct().ToList();
                    var found = await db.Destinations.CountAsync(d => distinct.Contains(d.Id) && d.IsActive, ct);
                    return found == distinct.Count;
                })
                .When(x => x.DestinationIds is { Count: > 0 } && ManualCount(x) <= MaxManualDestinations, ApplyConditionTo.CurrentValidator)
                .WithMessage(_ => T.Get("val.manualUnknown"));
            RuleForEach(x => x.DestinationCityCodes)
                .Must(code => code is { Length: 3 } && world.FindCity(code) is not null)
                .WithMessage(_ => T.Get("val.cityUnknown"));
        });

        When(x => x.DestinationMode == DestinationMode.Automatic, () =>
        {
            RuleFor(x => x.DestinationIds).Must((x, ids) => (ids is null || ids.Count == 0) && (x.DestinationCityCodes is null || x.DestinationCityCodes.Count == 0))
                .WithMessage(_ => T.Get("val.automaticNoSelection"));
        });

        RuleForEach(x => x.Continents).Must(c => Continents.Normalize(c) is not null)
            .WithMessage(_ => T.Get("val.continent", string.Join(", ", Continents.All)));

        When(x => x.Preferences is not null, () =>
        {
            RuleFor(x => x.Preferences!.MinAccommodationStars).InclusiveBetween(1, 5).WithMessage(_ => T.Get("val.stars"))
                .When(x => x.Preferences!.MinAccommodationStars.HasValue);
            RuleFor(x => x.Preferences!.MaxFlightDurationHours).InclusiveBetween(1, 48).WithMessage(_ => T.Get("val.flightHours"))
                .When(x => x.Preferences!.MaxFlightDurationHours.HasValue);
            RuleFor(x => x.Preferences!.MaxStops).InclusiveBetween(0, 3).WithMessage(_ => T.Get("val.maxStops"))
                .When(x => x.Preferences!.MaxStops.HasValue);
            RuleFor(x => x.Preferences!.PreferredDepartureTime).IsInEnum().WithMessage(_ => T.Get("val.departureTime"));
        });
    }
}

public sealed class CreateTripSearchHandler(
    IApplicationDbContext db,
    CityDestinationResolver cities,
    ITripSearchQueue queue,
    IDataModeAccessor dataMode,
    TimeProvider timeProvider) : IRequestHandler<CreateTripSearchCommand, CreateTripSearchResponse>
{
    public async Task<CreateTripSearchResponse> Handle(CreateTripSearchCommand request, CancellationToken cancellationToken)
    {
        var p = request.Preferences;
        var preferences = new TripPreferences
        {
            IncludeCheckedBaggage = p?.IncludeCheckedBaggage ?? false,
            MinAccommodationStars = p?.MinAccommodationStars,
            MaxFlightDurationHours = p?.MaxFlightDurationHours,
            MaxStops = p?.MaxStops,
            PreferredDepartureTime = p?.PreferredDepartureTime ?? DepartureTimePreference.Any,
            IncludeDailyExpenseEstimates = p?.IncludeDailyExpenseEstimates ?? false,
            AllowSelfTransfer = p?.AllowSelfTransfer ?? true
        };

        var search = new TripSearch(request.OriginAirportIata, request.EarliestDeparture, request.LatestReturn, request.DurationDays,
            request.Travelers, Math.Round(request.BudgetAmount, 2), request.BudgetCurrency, request.DestinationMode, preferences,
            dataMode.Mode, timeProvider.GetUtcNow());
        search.SetLanguage(T.CurrentLanguage);
        search.ReportProgress(0, T.Get("progress.queued"), timeProvider.GetUtcNow());

        if (request.DestinationMode == DestinationMode.Manual)
        {
            foreach (var id in (request.DestinationIds ?? []).Distinct()) search.SelectDestination(id);
            foreach (var code in (request.DestinationCityCodes ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var destination = await cities.ResolveAsync(code, cancellationToken)
                                  ?? throw new PlanningException(T.Get("val.cityUnknown"));
                search.SelectDestination(destination.Id);
            }
        }
        else
        {
            search.RestrictToContinents((request.Continents ?? []).Select(c => Continents.Normalize(c)!));
        }

        db.TripSearches.Add(search);
        await db.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(search.Id, cancellationToken);

        return new CreateTripSearchResponse(search.Id, search.Status, search.DataMode);
    }
}
