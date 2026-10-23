using TravelPlanner.Domain.Common;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Domain.Entities;

/// <summary>One priced component of a plan (flight, accommodation, transfer, fee, estimate).</summary>
public class TripPlanItem : Entity
{
    public Guid TripPlanId { get; set; }
    public PlanItemType Type { get; set; }
    public TransferDirection Direction { get; set; }
    public int SortOrder { get; set; }
    /// <summary>Budget category, e.g. "Flights" or "Taxes &amp; fees".</summary>
    public string Category { get; set; } = null!;
    /// <summary>Required categories with an unknown cost make the plan's budget incomplete.</summary>
    public bool IsRequired { get; set; }
    public string Title { get; set; } = null!;
    public string? Summary { get; set; }

    public string ProviderName { get; set; } = null!;
    /// <summary>Provider offer/property id or a stable itinerary signature, used for re-pricing.</summary>
    public string? ProviderOfferId { get; set; }

    /// <summary>Price as returned by the provider (null = unknown).</summary>
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    /// <summary>Price converted into the plan currency (equal to Price when currencies match).</summary>
    public decimal? ConvertedPrice { get; set; }
    public decimal? ExchangeRate { get; set; }
    public string? ExchangeRateSource { get; set; }
    public DateTimeOffset? ExchangeRateAsOfUtc { get; set; }
    /// <summary>False for informational lines, such as fees already included in another item's price.</summary>
    public bool IncludedInTotal { get; set; }

    /// <summary>Official provider URL; validated against the outbound-link allow-list before saving.</summary>
    public string? BookingUrl { get; set; }
    public DateTimeOffset RetrievedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public VerificationStatus Verification { get; set; }

    /// <summary>Normalized, application-owned details (never raw provider payloads).</summary>
    public string DetailsJson { get; set; } = "{}";

    public bool IsExpired(DateTimeOffset nowUtc) => ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= nowUtc;
}
