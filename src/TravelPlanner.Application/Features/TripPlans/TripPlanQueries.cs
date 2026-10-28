using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Application.Planning;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Features.TripPlans;

public sealed record GetTripPlanQuery(Guid Id) : IRequest<TripPlanDetailDto>;

public sealed record GetOutboundLinksQuery(Guid Id) : IRequest<IReadOnlyList<OutboundLinkDto>>;

internal static class TripPlanLoader
{
    public static async Task<TripPlan?> LoadAsync(IApplicationDbContext db, Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.TripPlans : db.TripPlans.AsNoTracking();
        var plan = await query
            .Include(p => p.Items)
            .Include(p => p.TripSearch)
            .Include(p => p.Destination).ThenInclude(d => d!.Airports)
            .Include(p => p.Destination).ThenInclude(d => d!.TransportLinks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        // A town's gateway airports, read-only only (on a tracked plan EF would move the airport to the town).
        if (!tracking && plan?.Destination is { } destination)
            await Features.TripSearches.GatewayAirports.LoadAsync(db, [destination], ct);
        return plan;
    }
}

public sealed class GetTripPlanHandler(IApplicationDbContext db, IOutboundLinkPolicy linkPolicy, TimeProvider timeProvider)
    : IRequestHandler<GetTripPlanQuery, TripPlanDetailDto>
{
    public async Task<TripPlanDetailDto> Handle(GetTripPlanQuery request, CancellationToken cancellationToken)
    {
        var plan = await TripPlanLoader.LoadAsync(db, request.Id, tracking: false, cancellationToken)
                   ?? throw new NotFoundException("resource.tripPlan", request.Id);
        var airports = await AirportLookup.LoadAsync(db, AirportLookup.CodesOf([plan]), cancellationToken);
        return plan.ToDetail(timeProvider.GetUtcNow(), linkPolicy, airports);
    }
}

/// <summary>
/// Official provider links for each offer. Only URLs that pass the allow-list are returned;
/// the server never fetches them, and clients must open them with rel="noopener noreferrer".
/// </summary>
public sealed class GetOutboundLinksHandler(IApplicationDbContext db, IOutboundLinkPolicy linkPolicy)
    : IRequestHandler<GetOutboundLinksQuery, IReadOnlyList<OutboundLinkDto>>
{
    public async Task<IReadOnlyList<OutboundLinkDto>> Handle(GetOutboundLinksQuery request, CancellationToken cancellationToken)
    {
        var plan = await TripPlanLoader.LoadAsync(db, request.Id, tracking: false, cancellationToken)
                   ?? throw new NotFoundException("resource.tripPlan", request.Id);

        var links = new List<OutboundLinkDto>();
        foreach (var item in plan.Items.OrderBy(i => i.SortOrder))
        {
            if (linkPolicy.Sanitize(item.BookingUrl) is { } booking)
                links.Add(new OutboundLinkDto(item.Id, item.Type, item.Direction, T.Get("link.book", item.Title), item.ProviderName, booking, item.Verification, "Booking"));

            // Combined one-way tickets: every ticket is booked on its own.
            if (item.Type == PlanItemType.Flight && TryDeserialize<FlightItemDetails>(item.DetailsJson) is { Tickets.Count: > 1 } flight)
            {
                foreach (var ticket in flight.Tickets!)
                {
                    if (linkPolicy.Sanitize(ticket.BookingUrl) is { } url && links.All(l => l.Url != url))
                        links.Add(new OutboundLinkDto(item.Id, item.Type, item.Direction, T.Get("link.bookTicket", ticket.From, ticket.To), item.ProviderName,
                            url, item.Verification, "Booking"));
                }
            }

            if (item.Type != PlanItemType.Transfer) continue;
            TransferItemDetails? details;
            try { details = JsonSerializer.Deserialize<TransferItemDetails>(item.DetailsJson, AppJson.Options); }
            catch (JsonException) { continue; }
            if (details is null) continue;

            if (linkPolicy.Sanitize(details.MapsDirectionsUrl) is { } maps)
                links.Add(new OutboundLinkDto(item.Id, item.Type, item.Direction, T.Get("link.directions", item.Title), "Google Maps", maps, VerificationStatus.Estimate, "Map"));

            foreach (var option in details.Options)
            {
                if (linkPolicy.Sanitize(option.Url) is { } url && links.All(l => l.Url != url))
                    links.Add(new OutboundLinkDto(item.Id, item.Type, item.Direction, option.Title, option.Provider, url, option.InfoVerification, "Information"));
            }
        }
        return links;
    }

    private static TDetails? TryDeserialize<TDetails>(string json) where TDetails : class
    {
        try { return JsonSerializer.Deserialize<TDetails>(json, AppJson.Options); }
        catch (JsonException) { return null; }
    }
}
