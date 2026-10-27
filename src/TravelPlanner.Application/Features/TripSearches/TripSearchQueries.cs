using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Features.TripSearches;

public sealed record GetTripSearchQuery(Guid Id) : IRequest<TripSearchDto>;

public enum PlanSort { Rank, TotalCost, RemainingBudget, DepartureDate, TotalCostDesc }

public sealed record GetTripSearchPlansQuery(Guid Id, int Page = 1, int PageSize = 10, PlanSort Sort = PlanSort.Rank)
    : IRequest<PagedResult<TripPlanSummaryDto>>;

public sealed record CancelTripSearchCommand(Guid Id) : IRequest<TripSearchDto>;

public sealed class GetTripSearchPlansValidator : AbstractValidator<GetTripSearchPlansQuery>
{
    public GetTripSearchPlansValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage(_ => T.Get("val.page"));
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50).WithMessage(_ => T.Get("val.pageSize", 50));
        RuleFor(x => x.Sort).IsInEnum().WithMessage(_ => T.Get("val.sort"));
    }
}

internal static class TripSearchLoader
{
    public static async Task<TripSearchDto> LoadDtoAsync(IApplicationDbContext db, TripSearch search, CancellationToken ct)
    {
        var ids = search.SelectedDestinations.Select(d => d.DestinationId).ToList();
        var selected = ids.Count == 0
            ? []
            : await db.Destinations.AsNoTracking().Where(d => ids.Contains(d.Id)).OrderBy(d => d.City).ToListAsync(ct);
        var planCount = await db.TripPlans.CountAsync(p => p.TripSearchId == search.Id, ct);
        return search.ToDto(selected, planCount);
    }
}

public sealed class GetTripSearchHandler(IApplicationDbContext db) : IRequestHandler<GetTripSearchQuery, TripSearchDto>
{
    public async Task<TripSearchDto> Handle(GetTripSearchQuery request, CancellationToken cancellationToken)
    {
        var search = await db.TripSearches.AsNoTracking().Include(s => s.SelectedDestinations)
                         .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
                     ?? throw new NotFoundException("resource.tripSearch", request.Id);
        return await TripSearchLoader.LoadDtoAsync(db, search, cancellationToken);
    }
}

public sealed class GetTripSearchPlansHandler(IApplicationDbContext db, TimeProvider timeProvider)
    : IRequestHandler<GetTripSearchPlansQuery, PagedResult<TripPlanSummaryDto>>
{
    public async Task<PagedResult<TripPlanSummaryDto>> Handle(GetTripSearchPlansQuery request, CancellationToken cancellationToken)
    {
        if (!await db.TripSearches.AnyAsync(s => s.Id == request.Id, cancellationToken))
            throw new NotFoundException("resource.tripSearch", request.Id);

        var query = db.TripPlans.AsNoTracking().Where(p => p.TripSearchId == request.Id);
        query = request.Sort switch
        {
            PlanSort.TotalCost => query.OrderBy(p => p.TotalEstimatedCost).ThenBy(p => p.Rank),
            PlanSort.TotalCostDesc => query.OrderByDescending(p => p.TotalEstimatedCost).ThenBy(p => p.Rank),
            PlanSort.RemainingBudget => query.OrderByDescending(p => p.RemainingBudget).ThenBy(p => p.Rank),
            PlanSort.DepartureDate => query.OrderBy(p => p.DepartureDate).ThenBy(p => p.Rank),
            _ => query.OrderBy(p => p.Rank)
        };

        var total = await query.CountAsync(cancellationToken);
        var plans = await query
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Include(p => p.Items).Include(p => p.Destination)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var airports = await AirportLookup.LoadAsync(db, AirportLookup.CodesOf(plans), cancellationToken);
        return new PagedResult<TripPlanSummaryDto>(plans.Select(p => p.ToSummary(now, airports)).ToList(), request.Page, request.PageSize, total);
    }
}

public sealed class CancelTripSearchHandler(
    IApplicationDbContext db,
    ISearchCancellationRegistry cancellationRegistry,
    TimeProvider timeProvider) : IRequestHandler<CancelTripSearchCommand, TripSearchDto>
{
    public async Task<TripSearchDto> Handle(CancelTripSearchCommand request, CancellationToken cancellationToken)
    {
        var search = await db.TripSearches.Include(s => s.SelectedDestinations)
                         .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
                     ?? throw new NotFoundException("resource.tripSearch", request.Id);

        if (search.IsFinished)
            throw new ConflictException(T.Get("api.alreadyFinished", search.Status));

        if (search.Status == TripSearchStatus.Queued)
        {
            search.Cancel(timeProvider.GetUtcNow());
            search.ReportProgress(search.ProgressPercent, T.Get("progress.cancelled"), timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // Running: the background worker observes the token and records the cancellation.
            cancellationRegistry.TryCancel(search.Id);
        }

        return await TripSearchLoader.LoadDtoAsync(db, search, cancellationToken);
    }
}
