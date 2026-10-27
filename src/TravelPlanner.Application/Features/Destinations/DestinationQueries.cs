using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Domain.Entities;

namespace TravelPlanner.Application.Features.Destinations;

public sealed record GetDestinationsQuery(string? Search, string? CountryCode, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<DestinationSummaryDto>>;

public sealed record GetDestinationByIdQuery(Guid Id) : IRequest<DestinationDetailDto>;

public sealed record GetAirportsQuery(bool DepartureOnly, string? Search) : IRequest<IReadOnlyList<AirportDto>>;

public sealed class GetDestinationsValidator : AbstractValidator<GetDestinationsQuery>
{
    public GetDestinationsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage(_ => T.Get("val.page"));
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => T.Get("val.pageSize", 100));
        RuleFor(x => x.Search).MaximumLength(100).WithMessage(_ => T.Get("val.searchLength"));
        RuleFor(x => x.CountryCode).Matches("^[A-Za-z]{2}$").WithMessage(_ => T.Get("val.countryCode"))
            .When(x => !string.IsNullOrEmpty(x.CountryCode));
    }
}

public sealed class GetAirportsValidator : AbstractValidator<GetAirportsQuery>
{
    public GetAirportsValidator() => RuleFor(x => x.Search).MaximumLength(100).WithMessage(_ => T.Get("val.searchLength"));
}

public sealed class GetDestinationsHandler(IApplicationDbContext db)
    : IRequestHandler<GetDestinationsQuery, PagedResult<DestinationSummaryDto>>
{
    public async Task<PagedResult<DestinationSummaryDto>> Handle(GetDestinationsQuery request, CancellationToken cancellationToken)
    {
        // The catalog is small (tens of cities), so filtering and sorting happen in memory: this lets users search
        // by English or translated names (e.g. "ვენა") and sort by the name shown in their language.
        var query = db.Destinations.AsNoTracking().Where(d => d.IsActive && d.IsCatalog);
        if (!string.IsNullOrWhiteSpace(request.CountryCode))
        {
            var cc = request.CountryCode.ToUpperInvariant();
            query = query.Where(d => d.CountryCode == cc);
        }
        var all = await query.Include(d => d.Airports).ToListAsync(cancellationToken);

        var language = T.CurrentLanguage;
        IEnumerable<Destination> filtered = all;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            bool Has(string? s) => s is not null && s.Contains(term, StringComparison.OrdinalIgnoreCase);
            filtered = all.Where(d => Has(d.City) || Has(d.Country) || Has(d.CityIn(language)) || Has(d.CountryIn(language)) ||
                                      d.Airports.Any(a => a.IataCode.Equals(term, StringComparison.OrdinalIgnoreCase)));
        }

        var sorted = filtered.OrderBy(d => d.CityIn(language), StringComparer.Create(T.CultureFor(language), true)).ToList();
        var page = sorted.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).Select(d => d.ToSummary()).ToList();
        return new PagedResult<DestinationSummaryDto>(page, request.Page, request.PageSize, sorted.Count);
    }
}

public sealed class GetDestinationByIdHandler(IApplicationDbContext db, IOutboundLinkPolicy linkPolicy)
    : IRequestHandler<GetDestinationByIdQuery, DestinationDetailDto>
{
    public async Task<DestinationDetailDto> Handle(GetDestinationByIdQuery request, CancellationToken cancellationToken)
    {
        var destination = await db.Destinations.AsNoTracking()
                              .Include(d => d.Airports).Include(d => d.TransportLinks).AsSplitQuery()
                              .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
                          ?? throw new NotFoundException("resource.destination", request.Id);
        return destination.ToDetail(linkPolicy);
    }
}

public sealed class GetAirportsHandler(IApplicationDbContext db) : IRequestHandler<GetAirportsQuery, IReadOnlyList<AirportDto>>
{
    public async Task<IReadOnlyList<AirportDto>> Handle(GetAirportsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Airports.AsNoTracking();
        if (request.DepartureOnly) query = query.Where(a => a.IsDepartureOption);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var lower = term.ToLowerInvariant();
            var upper = term.ToUpperInvariant();
            var language = T.CurrentLanguage;

            // Latin names and codes are matched in the database; translated names (e.g. "თბილისი") only exist on curated airports.
            var matches = await query
                .Where(a => a.IataCode == upper || a.CityCode == upper || a.City.ToLower().Contains(lower) || a.Name.ToLower().Contains(lower) ||
                            (a.Country != null && a.Country.ToLower().StartsWith(lower)))
                .Take(300).ToListAsync(cancellationToken);
            IEnumerable<Airport> translated = [];
            if (term.Any(c => c > 0x7F))
                translated = (await query.ToListAsync(cancellationToken))
                    .Where(a => a.Translations.Count > 0 &&
                                (a.CityIn(language).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                 a.NameIn(language).Contains(term, StringComparison.OrdinalIgnoreCase)));

            int Rank(Airport a) =>
                a.IataCode == upper ? 0 :
                a.CityCode == upper ? 1 :
                a.CityIn(language).Equals(term, StringComparison.OrdinalIgnoreCase) ? 2 :
                a.CityIn(language).StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 3 :
                a.CityIn(language).Contains(term, StringComparison.OrdinalIgnoreCase) ? 4 :
                a.NameIn(language).Contains(term, StringComparison.OrdinalIgnoreCase) ? 5 : 6;

            return matches.Concat(translated).DistinctBy(a => a.IataCode)
                .OrderBy(Rank).ThenByDescending(a => a.DestinationId != null || a.Translations.Count > 0).ThenBy(a => a.CityIn(language)).ThenBy(a => a.IataCode)
                .Take(20).Select(a => a.ToDto()).ToList();
        }
        // Without a search term: the curated airports only (those with translated names); the selector searches the rest.
        var airports = (await query.ToListAsync(cancellationToken)).Where(a => a.Translations.Count > 0);
        return airports.OrderBy(a => a.CityIn(T.CurrentLanguage)).ThenBy(a => a.IataCode).Take(200).Select(a => a.ToDto()).ToList();
    }
}
