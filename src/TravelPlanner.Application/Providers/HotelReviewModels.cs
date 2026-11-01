namespace TravelPlanner.Application.Providers;

/// <summary>
/// Recent guest reviews of a hotel from a review platform (Tripadvisor). The hotel is found by its name and position,
/// because the stay a visitor picks comes from map data, not from the review platform.
/// Review texts must not be stored: they are fetched when shown.
/// </summary>
public interface IHotelReviewSource
{
    bool IsConfigured { get; }

    /// <summary>Null when the source is not configured or failed; a result with Matched = false when the hotel is unknown there.</summary>
    Task<HotelReviewResult?> GetRecentAsync(HotelReviewQuery query, CancellationToken cancellationToken);
}

public sealed record HotelReviewQuery(string Name, double Latitude, double Longitude, string Language);

public sealed record HotelReviewResult(
    string Source,
    bool Matched,
    string? HotelName,
    string? HotelUrl,
    IReadOnlyList<HotelReview> Reviews);

/// <summary>One review as the platform published it (text unchanged, in its own language).</summary>
public sealed record HotelReview(
    string Id,
    int Rating,
    string? Title,
    string Text,
    string Language,
    DateTimeOffset PublishedAt,
    string? TravelDate,
    string? TripType,
    string? Author,
    string? AuthorLocation,
    string? AvatarUrl,
    string? Url,
    string? RatingImageUrl,
    string? OwnerResponse);

/// <summary>Used when no review platform is configured: the UI then hides the reviews block.</summary>
public sealed class NoHotelReviewSource : IHotelReviewSource
{
    public bool IsConfigured => false;
    public Task<HotelReviewResult?> GetRecentAsync(HotelReviewQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<HotelReviewResult?>(null);
}
