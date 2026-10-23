namespace TravelPlanner.Domain.Entities;

/// <summary>Translated catalog texts for one language (missing values fall back to the default English text).</summary>
public sealed class DestinationTranslation
{
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Description { get; set; }
}

public sealed class AirportTranslation
{
    public string? Name { get; set; }
    public string? City { get; set; }
}

internal static class Localized
{
    public static string Pick(IReadOnlyDictionary<string, string>? translations, string language, string fallback) =>
        translations is not null && translations.TryGetValue(language, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
