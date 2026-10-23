using Microsoft.EntityFrameworkCore;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Common;

public interface IApplicationDbContext
{
    DbSet<Destination> Destinations { get; }
    DbSet<Airport> Airports { get; }
    DbSet<DestinationTransportLink> DestinationTransportLinks { get; }
    DbSet<TripSearch> TripSearches { get; }
    DbSet<TripPlan> TripPlans { get; }
    DbSet<TripPlanItem> TripPlanItems { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Queue of trip searches waiting for background processing.</summary>
public interface ITripSearchQueue
{
    ValueTask EnqueueAsync(Guid tripSearchId, CancellationToken cancellationToken = default);
}

/// <summary>Tracks cancellation of running searches so the API can cancel them.</summary>
public interface ISearchCancellationRegistry
{
    CancellationToken Register(Guid tripSearchId, CancellationToken stoppingToken);
    bool TryCancel(Guid tripSearchId);
    void Unregister(Guid tripSearchId);
}

/// <summary>Persists progress for a running search out-of-band (independent of the main unit of work).</summary>
public interface ISearchProgressReporter
{
    Task ReportAsync(Guid tripSearchId, int percent, string message, CancellationToken cancellationToken);
}

/// <summary>Current data mode (demo vs. live provider data).</summary>
public interface IDataModeAccessor
{
    DataMode Mode { get; }
}
