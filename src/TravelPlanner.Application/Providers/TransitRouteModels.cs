namespace TravelPlanner.Application.Providers;

/// <summary>
/// Public-transport journey request; Time is the wished local departure (date and time) at the origin. AnyDistance keeps
/// intercity trains and long journeys (the free-form planner), which airport and in-city routes drop.
/// </summary>
public sealed record TransitRouteQuery(double FromLatitude, double FromLongitude, double ToLatitude, double ToLongitude, DateTime LocalDeparture,
    TimeSpan UtcOffset, bool AnyDistance = false);

/// <summary>
/// One step of a journey. Mode is WALK, BUS, COACH, SUBWAY, METRO, TRAM, RAIL, SUBURBAN, REGIONAL_RAIL, FERRY …;
/// Line is the public line name ("A2", "L1", "Elizabeth line"); Stops counts the stops ridden.
/// </summary>
public sealed record TransitLeg(
    string Mode,
    string? Line,
    string? Headsign,
    string From,
    string To,
    DateTime DepartureLocal,
    DateTime ArrivalLocal,
    int DurationMinutes,
    string? Agency,
    int? Stops,
    string? Color);

public sealed record TransitItinerary(int DurationMinutes, int Transfers, DateTime DepartureLocal, DateTime ArrivalLocal, IReadOnlyList<TransitLeg> Legs);

/// <summary>Timetable-based journey planner (journeys follow the current timetables, which may differ on the travel date).</summary>
public interface ITransitRouter
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<TransitItinerary>> PlanAsync(TransitRouteQuery query, CancellationToken cancellationToken);
}

/// <summary>Text in English and Georgian.</summary>
public sealed record LocalizedText(string? En, string? Ka)
{
    public string? In(string language) => language == "ka" && !string.IsNullOrWhiteSpace(Ka) ? Ka : En;
}

public sealed record TransitGuideOption(
    string Mode,
    string Name,
    string? From,
    string? To,
    LocalizedText? Ticket,
    decimal? Price,
    string? Currency,
    LocalizedText? WhereToBuy,
    LocalizedText? Payment,
    LocalizedText? Duration,
    LocalizedText? Frequency,
    string? Url,
    IReadOnlyList<string>? Operators = null,
    IReadOnlyList<string>? Lines = null,
    bool? BuyOnline = null,
    string? BuyUrl = null,
    LocalizedText? BuyNote = null);

public sealed record TransitGuideFare(LocalizedText? Name, decimal? Price, string? Currency);

public sealed record TransitGuideApp(string Name, string? Url);

/// <summary>How to get from a city's airport into town and which tickets to buy, researched from official sources.</summary>
public sealed record TransitGuide(
    string Airport,
    IReadOnlyList<TransitGuideOption> Options,
    TransitGuideFare? CityTicket,
    TransitGuideFare? DayPass,
    bool? Contactless,
    IReadOnlyList<TransitGuideApp> Apps,
    IReadOnlyList<LocalizedText> Tips,
    IReadOnlyList<string> Sources,
    string? CheckedOn,
    bool? CityBuyOnline = null,
    string? CityBuyUrl = null,
    LocalizedText? CityBuyNote = null);

public interface ITransitGuideSource
{
    TransitGuide? Get(string destinationCode);
}
