using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Domain.Entities;

namespace TravelPlanner.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<Airport> Airports => Set<Airport>();
    public DbSet<DestinationTransportLink> DestinationTransportLinks => Set<DestinationTransportLink>();
    public DbSet<TripSearch> TripSearches => Set<TripSearch>();
    public DbSet<TripPlan> TripPlans => Set<TripPlan>();
    public DbSet<TripPlanItem> TripPlanItems => Set<TripPlanItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
