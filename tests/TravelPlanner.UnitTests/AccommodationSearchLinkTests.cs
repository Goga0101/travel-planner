using Microsoft.Extensions.Options;
using TravelPlanner.Application.Planning;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Infrastructure.Providers.BookingCom;
using static TravelPlanner.UnitTests.TestData;

namespace TravelPlanner.UnitTests;

public class AccommodationSearchLinkTests
{
    private static BookingComSearchLinkBuilder Builder(BookingComLinkOptions options) => new(Options.Create(options));

    [Fact]
    public void Builds_Prefilled_Booking_Search_Url()
    {
        var link = Builder(new BookingComLinkOptions()).Build("Vienna", "Austria", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), 2, 1)!;

        Assert.Equal("Booking.com", link.Provider);
        Assert.Equal("https://www.booking.com/searchresults.html?ss=Vienna%2C%20Austria&checkin=2026-11-20&checkout=2026-11-24" +
                     "&group_adults=2&no_rooms=1&group_children=0", link.Url);
        Assert.NotNull(LinkPolicy("booking.com").Sanitize(link.Url));
    }

    [Fact]
    public void Adds_Affiliate_Id_When_Configured()
    {
        var link = Builder(new BookingComLinkOptions { AffiliateId = "123456" })
            .Build("Rome", "Italy", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), 1, 1)!;

        Assert.EndsWith("&aid=123456", link.Url);
    }

    [Fact]
    public void Wraps_Url_In_Affiliate_Network_Deep_Link()
    {
        var link = Builder(new BookingComLinkOptions { DeepLinkTemplate = "https://www.awin1.com/cread.php?awinmid=1&awinaffid=2&ued={url}" })
            .Build("Rome", "Italy", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), 1, 1)!;

        Assert.StartsWith("https://www.awin1.com/cread.php?awinmid=1&awinaffid=2&ued=https%3A%2F%2Fwww.booking.com%2Fsearchresults.html%3Fss%3DRome", link.Url);
    }

    [Fact]
    public void Disabled_Returns_Null()
    {
        Assert.Null(Builder(new BookingComLinkOptions { Enabled = false })
            .Build("Rome", "Italy", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), 1, 1));
    }

    [Fact]
    public async Task Plans_Without_Accommodation_Data_Get_Search_Link_But_Stay_Incomplete()
    {
        var flights = new FakeFlightProvider(r => [Flight(r.OriginIata, r.DestinationIata, r.DepartureDate, r.ReturnDate, 200)]);
        var engine = EngineFactory.Create([flights], searchLinks: Builder(new BookingComLinkOptions()));

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), null, CancellationToken.None);

        Assert.All(result.Plans, p =>
        {
            var stay = p.Drafts.Single(d => d.Type == PlanItemType.Accommodation);
            Assert.Null(stay.Amount);
            Assert.Equal(VerificationStatus.Unavailable, stay.Verification);
            Assert.StartsWith("https://www.booking.com/searchresults.html?ss=Rome", stay.BookingUrl);
            Assert.Contains("checkin=2026-11-", stay.BookingUrl);
            Assert.Equal(BudgetFitStatus.WithinBudgetIncomplete, p.Budget.Fit);
            Assert.Contains(CostCategories.Accommodation, p.Budget.MissingCategories);
        });
    }
}
