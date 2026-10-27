using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.UnitTests;

public class LegTicketTests
{
    private static readonly TransitGuide Barcelona = new(
        "BCN",
        [
            new TransitGuideOption("metro", "Metro L9 Sud", null, null, new LocalizedText("Airport ticket", "აეროპორტის ბილეთი"), 5.90m, "EUR",
                null, null, null, null, "https://tmb.cat/airport", ["TMB"], ["L9 Sud", "L9"], true, "https://tmb.cat/airport-ticket", null),
            new TransitGuideOption("express_bus", "Aerobús A1 / A2", null, null, new LocalizedText("Aerobús single", null), null, "EUR",
                null, null, null, null, "https://aerobusbarcelona.es/", ["Aerobús"], ["A1", "A2"], true, "https://aerobusbarcelona.es/", null),
        ],
        new TransitGuideFare(new LocalizedText("Single ticket", "ერთჯერადი"), 2.90m, "EUR"), null, true, [], [], [], "2026-10-27",
        CityBuyOnline: true, CityBuyUrl: "https://tmb.cat/app", CityBuyNote: new LocalizedText("TMB App", null));

    private static TransitLegDto Leg(string mode, string? line, string? agency) =>
        new(mode, line, null, "A", "B", "09:00", "09:20", 20, agency, 3, null);

    [Fact]
    public void A_Line_Code_Picks_Its_Airport_Ticket()
    {
        var ticket = GetStayRouteHandler.TicketFor(Leg("BUS", "A2", "Monbus (Aerobús)"), Barcelona, "en")!;
        Assert.Equal("Aerobús single", ticket.Name);
        Assert.Equal("https://aerobusbarcelona.es/", ticket.BuyUrl);
        Assert.False(ticket.CityTicket);
    }

    [Fact]
    public void The_Same_Operator_On_Another_Line_Needs_The_City_Ticket()
    {
        // TMB runs both L9 Sud (airport ticket) and L1 (normal city ticket).
        var ticket = GetStayRouteHandler.TicketFor(Leg("SUBWAY", "L1", "TMB"), Barcelona, "ka")!;
        Assert.True(ticket.CityTicket);
        Assert.Equal("ერთჯერადი", ticket.Name);
        Assert.Equal(2.90m, ticket.Price);
        Assert.Equal("https://tmb.cat/app", ticket.BuyUrl);
    }

    [Fact]
    public void Regional_Trains_Without_A_Match_Get_No_Ticket()
    {
        Assert.Null(GetStayRouteHandler.TicketFor(Leg("REGIONAL_RAIL", "R2N", "Renfe"), Barcelona, "en"));
    }
}
