namespace TravelPlanner.Application.Features.Transit;

/// <summary>A taxi / ride-hailing app and the countries (ISO 3166-1 alpha-2) where it is a common choice.</summary>
public sealed record RideHailingApp(string Name, string Url, IReadOnlySet<string> Countries);

/// <summary>
/// Hand-curated list of the best-known taxi and ride-hailing apps per country. Operators enter and leave markets and
/// coverage differs between cities, so the UI says to check availability in the app. Unknown countries get the global apps.
/// </summary>
public static class RideHailingApps
{
    private static HashSet<string> C(string codes) => new(codes.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyList<RideHailingApp> All =
    [
        new("Bolt", "https://bolt.eu/",
            C("GE AZ KZ UZ UA PL EE LV LT CZ SK HU RO BG HR SI RS GR CY MT PT ES FR DE AT BE NL SE NO FI IE GB IT ZA NG KE GH TZ UG")),
        new("Uber", "https://www.uber.com/",
            C("US CA MX BR AR CL CO PE EC CR DO GT PA UY GB IE FR DE NL BE ES PT IT AT CH PL CZ SK RO HR GR TR UA AE SA QA KW BH OM EG JO IN BD LK JP KR TW HK AU NZ ZA NG KE GH")),
        new("Yandex Go", "https://go.yandex/", C("GE AM AZ KZ UZ KG BY RS RU")),
        new("Maxim", "https://taximaxim.com/", C("GE AZ KZ UZ KG TJ RU ID MY PH")),
        new("inDrive", "https://indrive.com/", C("KZ KG UZ AZ MX CO PE BR BO EG MA PK IN NP ID TH")),
        new("FREENOW", "https://www.free-now.com/", C("DE AT IE GB ES FR IT PL GR")),
        new("Lyft", "https://www.lyft.com/", C("US CA")),
        new("Grab", "https://www.grab.com/", C("SG MY ID TH VN PH MM KH")),
        new("Gojek", "https://www.gojek.com/", C("ID SG")),
        new("Careem", "https://www.careem.com/", C("AE SA QA KW BH OM JO PK")),
        new("DiDi", "https://web.didiglobal.com/", C("CN MX BR CL CO PE AU NZ JP")),
        new("Cabify", "https://cabify.com/", C("ES AR CL CO PE UY")),
        new("BiTaksi", "https://www.bitaksi.com/", C("TR")),
        new("Ola", "https://www.olacabs.com/", C("IN")),
        new("Rapido", "https://www.rapido.bike/", C("IN")),
        new("Kakao T", "https://www.kakaomobility.com/", C("KR")),
        new("GO", "https://go.goinc.jp/", C("JP")),
        new("Heetch", "https://www.heetch.com/", C("FR BE MA DZ SN")),
    ];

    /// <summary>Apps for the country (in the order above); Uber and Bolt when the country is unknown or has none listed.</summary>
    public static IReadOnlyList<RideHailingApp> For(string? countryCode)
    {
        var apps = countryCode is null ? [] : All.Where(a => a.Countries.Contains(countryCode)).ToList();
        return apps.Count > 0 ? apps : All.Where(a => a.Name is "Uber" or "Bolt").ToList();
    }
}
