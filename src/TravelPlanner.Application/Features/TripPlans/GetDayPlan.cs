using System.Globalization;
using System.Text.Json;
using FluentValidation;
using MediatR;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Planning;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Features.TripPlans;

/// <summary>A day-by-day itinerary for a plan, built for the chosen interests and pace.</summary>
/// <param name="Music">Music genres to keep among the music events (provider genre names); empty = all.</param>
/// <param name="FromLatitude">Where each day starts (the chosen hotel); the city centre when omitted.</param>
public sealed record GetDayPlanQuery(Guid PlanId, IReadOnlyList<string> Interests, TripPace Pace, IReadOnlyList<string>? Music = null,
    double? FromLatitude = null, double? FromLongitude = null)
    : IRequest<DayPlanDto>;

/// <summary>A music genre found among the city's events, with how many events have it.</summary>
public sealed record MusicGenreDto(string Name, int Count);

public sealed record ActivityPlaceDto(
    string Id,
    string Name,
    string Interest,
    string Kind,
    double Latitude,
    double Longitude,
    string? Address,
    string? OpeningHours,
    string? Website,
    string? Cuisine,
    string? ImageUrl,
    string? Summary,
    bool Notable,
    int DistanceMeters,
    string? MapUrl,
    string? DirectionsUrl);

public sealed record CityEventDto(
    string Id,
    string Name,
    DateOnly Date,
    string? Time,
    string? Venue,
    string? Segment,
    string? Genre,
    string? ImageUrl,
    decimal? PriceMin,
    decimal? PriceMax,
    string? Currency,
    string? Url,
    string? DirectionsUrl,
    string? SubGenre,
    double? Latitude = null,
    double? Longitude = null);

public sealed record DayItemDto(string Slot, string Time, ActivityPlaceDto? Place, CityEventDto? Event, IReadOnlyList<ActivityPlaceDto> Alternatives);

public sealed record PlanDayDto(int Number, DateOnly Date, PlanDayKind Kind, IReadOnlyList<DayItemDto> Items);

/// <summary>EventsConfigured is false when no event provider is set up; EventsSearchUrl then points to an events search for the city and dates.</summary>
public sealed record DayPlanDto(
    IReadOnlyList<string> Interests,
    TripPace Pace,
    IReadOnlyList<PlanDayDto> Days,
    IReadOnlyList<CityEventDto> Events,
    bool EventsConfigured,
    string? EventsProvider,
    string EventsSearchUrl,
    string? ArrivalTime,
    string? DepartureTime,
    IReadOnlyList<MusicGenreDto> MusicGenres,
    IReadOnlyList<string> Music,
    string? ResidentAdvisorUrl,
    double StartLatitude = 0,
    double StartLongitude = 0);

public sealed class GetDayPlanValidator : AbstractValidator<GetDayPlanQuery>
{
    public GetDayPlanValidator()
    {
        RuleForEach(x => x.Interests).Must(i => Interests.Normalize(i) is not null).WithMessage(_ => T.Get("val.interest", string.Join(", ", Interests.All)));
        RuleFor(x => x.Interests.Count).LessThanOrEqualTo(Interests.All.Count);
        RuleFor(x => x.Pace).IsInEnum();
        RuleFor(x => x.Music).Must(m => m is null || m.Count <= 20);
        RuleFor(x => x.FromLatitude).InclusiveBetween(-90, 90).When(x => x.FromLatitude.HasValue);
        RuleFor(x => x.FromLongitude).InclusiveBetween(-180, 180).When(x => x.FromLongitude.HasValue);
        RuleForEach(x => x.Music).MaximumLength(60);
    }
}

public sealed class GetDayPlanHandler(
    IApplicationDbContext db,
    IActivityPlaceSource places,
    IEventSource events,
    IOutboundLinkPolicy linkPolicy) : IRequestHandler<GetDayPlanQuery, DayPlanDto>
{
    public async Task<DayPlanDto> Handle(GetDayPlanQuery request, CancellationToken cancellationToken)
    {
        var plan = await TripPlanLoader.LoadAsync(db, request.PlanId, tracking: false, cancellationToken)
                   ?? throw new NotFoundException("resource.tripPlan", request.PlanId);
        var destination = plan.Destination!;
        var interests = request.Interests.Select(Interests.Normalize).OfType<string>().Distinct().ToList();
        if (interests.Count == 0) interests = [Interests.Culture, Interests.Food];

        // Arrival and departure wall-clock times come from the flight, when the provider reported them.
        FlightItemDetails? flight = null;
        if (plan.Items.FirstOrDefault(i => i.Type == PlanItemType.Flight) is { } item)
        {
            try { flight = JsonSerializer.Deserialize<FlightItemDetails>(item.DetailsJson, AppJson.Options); }
            catch (JsonException) { }
        }
        var arrival = flight?.Outbound.ArrivalLocal;
        var departure = flight?.Inbound.DepartureLocal;

        var language = T.CurrentLanguage;
        var placesTask = places.GetAsync(new ActivityQuery(destination.Code, destination.Latitude, destination.Longitude, interests, language), cancellationToken);
        var eventsTask = events.IsConfigured
            ? events.GetAsync(new EventQuery(destination.City, destination.CountryCode, destination.Latitude, destination.Longitude,
                plan.DepartureDate, plan.ReturnDate, language), cancellationToken)
            : Task.FromResult<IReadOnlyList<CityEvent>>([]);
        await Task.WhenAll(placesTask, eventsTask);
        var allEvents = eventsTask.Result.OrderBy(e => e.Date).ThenBy(e => e.Time).ToList();

        // Music genres present in the city's events; a genre filter keeps the music events of those genres (and every non-music event).
        var genres = allEvents.Where(IsMusic).Select(e => e.Genre ?? "Other").GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MusicGenreDto(g.Key, g.Count())).OrderByDescending(g => g.Count).ThenBy(g => g.Name).ToList();
        var music = (request.Music ?? []).Where(m => !string.IsNullOrWhiteSpace(m))
            .Where(m => genres.Any(g => g.Name.Equals(m, StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var cityEvents = music.Count == 0
            ? allEvents
            : allEvents.Where(e => !IsMusic(e) || music.Contains(e.Genre ?? "Other", StringComparer.OrdinalIgnoreCase)).ToList();

        var days = DayPlanner.Build(
            new DayPlanRequest(plan.DepartureDate, plan.ReturnDate, arrival, departure, interests, request.Pace,
                request.FromLatitude ?? destination.Latitude, request.FromLongitude ?? destination.Longitude),
            placesTask.Result, cityEvents);

        return new DayPlanDto(
            interests,
            request.Pace,
            days.Select(d => new PlanDayDto(d.Number, d.Date, d.Kind, d.Items.Select(i => new DayItemDto(
                i.Slot,
                i.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
                i.Place is null ? null : ToDto(i.Place, destination.City),
                i.Event is null ? null : ToDto(i.Event),
                i.Alternatives.Select(a => ToDto(a, destination.City)).ToList())).ToList())).ToList(),
            cityEvents.Select(ToDto).ToList(),
            events.IsConfigured,
            events.IsConfigured ? events.Name : null,
            EventsSearchUrl(destination.CityIn("en"), plan.DepartureDate),
            arrival?.ToString("HH:mm", CultureInfo.InvariantCulture),
            departure?.ToString("HH:mm", CultureInfo.InvariantCulture),
            genres,
            music,
            ResidentAdvisorUrl(destination.CountryCode, destination.CityIn("en")),
            request.FromLatitude ?? destination.Latitude,
            request.FromLongitude ?? destination.Longitude);
    }

    private static bool IsMusic(CityEvent e) => (e.Segment ?? "").Contains("Music", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resident Advisor's events page of the city (ra.co/events/{country}/{area}). RA has no public API and blocks automated
    /// access, so this is only a link the visitor opens; RA uses "uk" for the United Kingdom and city names without spaces.
    /// </summary>
    public static string ResidentAdvisorUrl(string countryCode, string city)
    {
        var country = countryCode.Equals("GB", StringComparison.OrdinalIgnoreCase) ? "uk" : countryCode.ToLowerInvariant();
        var normalized = city.Normalize(System.Text.NormalizationForm.FormD);
        var area = new string(normalized.Where(c => char.IsAsciiLetterOrDigit(c)).ToArray()).ToLowerInvariant();
        return $"https://ra.co/events/{country}/{area}";
    }

    private ActivityPlaceDto ToDto(PlannedPlace p, string city)
    {
        var place = p.Place;
        return new ActivityPlaceDto(place.Id, place.Name, place.Interest, place.Kind, place.Latitude, place.Longitude, place.Address,
            place.OpeningHours, NearbyLinks.CuratedHttps(place.Website), place.Cuisine, NearbyLinks.CuratedHttps(place.ImageUrl), place.Summary,
            place.Notable, p.DistanceMeters,
            linkPolicy.Sanitize(NearbyLinks.MapSearch($"{place.Name}, {city}")),
            linkPolicy.Sanitize(NearbyLinks.TransitDirections(p.FromLatitude, p.FromLongitude, place.Latitude, place.Longitude)));
    }

    private CityEventDto ToDto(CityEvent e) =>
        new(e.Id, e.Name, e.Date, e.Time?.ToString("HH:mm", CultureInfo.InvariantCulture), e.Venue, e.Segment, e.Genre,
            NearbyLinks.CuratedHttps(e.ImageUrl), e.PriceMin, e.PriceMax, e.Currency, NearbyLinks.CuratedHttps(e.Url),
            e.Latitude is { } lat && e.Longitude is { } lon ? linkPolicy.Sanitize(NearbyLinks.TransitDirectionsTo(lat, lon)) : null,
            e.SubGenre, e.Latitude, e.Longitude);

    /// <summary>Without an event provider: a web search for events in the city during the trip month.</summary>
    public static string EventsSearchUrl(string city, DateOnly month) =>
        "https://www.google.com/search?q=" + Uri.EscapeDataString(
            $"events {city} {month.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}");
}
