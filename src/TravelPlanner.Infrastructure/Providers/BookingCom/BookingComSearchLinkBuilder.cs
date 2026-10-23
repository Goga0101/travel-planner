using Microsoft.Extensions.Options;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.BookingCom;

public sealed class BookingComLinkOptions
{
    public const string SectionName = "Providers:BookingComLinks";

    public bool Enabled { get; set; } = true;
    public string SearchUrl { get; set; } = "https://www.booking.com/searchresults.html";
    /// <summary>Booking.com affiliate id ("aid") for direct affiliate tracking. Optional.</summary>
    public string? AffiliateId { get; set; }
    /// <summary>
    /// Optional affiliate-network deep-link template (CJ / Awin) containing "{url}", which is replaced with the
    /// URL-encoded Booking.com search URL. Its host must also be listed in OutboundLinks:AllowedHosts.
    /// </summary>
    public string? DeepLinkTemplate { get; set; }
}

/// <summary>Builds a Booking.com search-results URL pre-filled with city, dates and guests. No API calls, no scraping.</summary>
public sealed class BookingComSearchLinkBuilder(IOptions<BookingComLinkOptions> options) : IAccommodationSearchLinkBuilder
{
    public const string ProviderName = "Booking.com";

    public AccommodationSearchLink? Build(string city, string country, DateOnly checkIn, DateOnly checkOut, int adults, int rooms)
    {
        var o = options.Value;
        if (!o.Enabled || !ProviderHttp.HasValue(o.SearchUrl)) return null;

        var query = new List<KeyValuePair<string, string>>
        {
            new("ss", $"{city}, {country}"),
            new("checkin", ProviderHttp.Iso(checkIn)),
            new("checkout", ProviderHttp.Iso(checkOut)),
            new("group_adults", adults.ToString()),
            new("no_rooms", rooms.ToString()),
            new("group_children", "0")
        };
        if (ProviderHttp.HasValue(o.AffiliateId)) query.Add(new("aid", o.AffiliateId!));

        var url = o.SearchUrl + "?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        if (ProviderHttp.HasValue(o.DeepLinkTemplate) && o.DeepLinkTemplate!.Contains("{url}"))
            url = o.DeepLinkTemplate.Replace("{url}", Uri.EscapeDataString(url));

        return new AccommodationSearchLink(ProviderName, url);
    }
}
