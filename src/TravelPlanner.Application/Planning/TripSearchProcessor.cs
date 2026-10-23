using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Planning;

/// <summary>Executes one queued trip search and persists its plans.</summary>
public sealed class TripSearchProcessor(
    IApplicationDbContext db,
    TripPlanningEngine engine,
    ISearchProgressReporter progressReporter,
    IOutboundLinkPolicy linkPolicy,
    IOptions<PlanningOptions> options,
    TimeProvider timeProvider,
    ILogger<TripSearchProcessor> logger)
{
    public async Task ProcessAsync(Guid tripSearchId, CancellationToken cancellationToken)
    {
        var search = await db.TripSearches
            .Include(s => s.SelectedDestinations)
            .FirstOrDefaultAsync(s => s.Id == tripSearchId, cancellationToken);
        if (search is null || search.Status != TripSearchStatus.Queued) return;

        // All server-generated texts of this search use the language it was created in.
        using var language = T.Use(search.Language);
        search.MarkRunning(timeProvider.GetUtcNow());
        search.ReportProgress(1, T.Get("progress.started"), timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var origin = await db.Airports.AsNoTracking().FirstOrDefaultAsync(a => a.IataCode == search.OriginAirportIata, cancellationToken)
                         ?? throw new PlanningException(T.Get("error.unknownOrigin", search.OriginAirportIata));

            var query = db.Destinations.AsNoTracking()
                .Include(d => d.Airports)
                .Include(d => d.TransportLinks)
                .AsSplitQuery()
                .Where(d => d.IsActive);

            List<Destination> destinations;
            if (search.DestinationMode == DestinationMode.Manual)
            {
                var ids = search.SelectedDestinations.Select(d => d.DestinationId).ToList();
                destinations = await query.Where(d => ids.Contains(d.Id)).OrderBy(d => d.City).ToListAsync(cancellationToken);
            }
            else
            {
                destinations = await query
                    .Where(d => origin.DestinationId == null || d.Id != origin.DestinationId)
                    .OrderByDescending(d => d.Popularity).ThenBy(d => d.City)
                    .Take(options.Value.MaxAutoDestinations)
                    .ToListAsync(cancellationToken);
            }

            var request = new PlanningRequest(origin, destinations, search.EarliestDeparture, search.LatestReturn, search.DurationDays,
                search.Travelers, search.BudgetAmount, search.BudgetCurrency, search.Preferences);

            var result = await engine.PlanAsync(request,
                p => progressReporter.ReportAsync(search.Id, p.Percent, p.Message, cancellationToken), cancellationToken);

            foreach (var warning in result.Warnings) search.AddWarning(warning);
            var now = timeProvider.GetUtcNow();
            var rank = 1;
            foreach (var candidate in result.Plans)
            {
                db.TripPlans.Add(PlanEntityFactory.Create(search.Id, candidate, rank++, search.Travelers, search.BudgetCurrency,
                    search.BudgetAmount, now, linkPolicy));
            }

            search.Complete(result.AnyWithinBudget, now);
            search.ReportProgress(100, T.Get(result.AnyWithinBudget ? "progress.completed" : "progress.noMatch"), now);
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogInformation("Trip search {SearchId} finished with status {Status} and {Count} plan(s)", search.Id, search.Status, result.Plans.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            search.Cancel(timeProvider.GetUtcNow());
            search.ReportProgress(search.ProgressPercent, T.Get("progress.cancelled"), timeProvider.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogInformation("Trip search {SearchId} was cancelled", search.Id);
        }
        catch (PlanningException ex)
        {
            search.Fail(ex.Message, timeProvider.GetUtcNow());
            search.ReportProgress(search.ProgressPercent, T.Get("progress.failed"), timeProvider.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogInformation("Trip search {SearchId} could not be planned: {Reason}", search.Id, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Trip search {SearchId} failed unexpectedly", search.Id);
            search.Fail(T.Get("error.unexpected"), timeProvider.GetUtcNow());
            search.ReportProgress(search.ProgressPercent, T.Get("progress.failed"), timeProvider.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
