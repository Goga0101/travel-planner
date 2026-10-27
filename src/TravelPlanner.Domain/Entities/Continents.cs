namespace TravelPlanner.Domain.Entities;

/// <summary>Continent keys used by destinations (Region) and airports.</summary>
public static class Continents
{
    public const string Europe = "Europe";
    public const string Asia = "Asia";
    public const string Africa = "Africa";
    public const string NorthAmerica = "NorthAmerica";
    public const string SouthAmerica = "SouthAmerica";
    public const string Oceania = "Oceania";

    public static readonly IReadOnlyList<string> All = [Europe, Asia, Africa, NorthAmerica, SouthAmerica, Oceania];

    public static string? Normalize(string? value) =>
        All.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
}
