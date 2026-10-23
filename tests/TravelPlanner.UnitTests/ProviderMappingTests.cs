using System.Text.Json;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Infrastructure.ExchangeRates;
using TravelPlanner.Infrastructure.Providers.Amadeus;
using TravelPlanner.Infrastructure.Providers.BookingCom;
using TravelPlanner.Infrastructure.Providers.Google;
using TravelPlanner.Infrastructure.Providers.Travelpayouts;
using static TravelPlanner.UnitTests.TestData;

namespace TravelPlanner.UnitTests;

public class ProviderMappingTests
{
    private static readonly FlightSearchRequest Request = new("TBS", "VIE", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), 2, "EUR", null, false, 10);

    [Fact]
    public void Amadeus_Offer_Is_Normalized()
    {
        const string json = """
        {
          "data": [{
            "id": "1",
            "itineraries": [
              { "duration": "PT6H25M", "segments": [
                { "departure": { "iataCode": "TBS", "at": "2026-11-20T04:30:00" }, "arrival": { "iataCode": "IST", "at": "2026-11-20T06:15:00" }, "carrierCode": "TK", "number": "381", "duration": "PT2H45M", "numberOfStops": 0 },
                { "departure": { "iataCode": "IST", "at": "2026-11-20T08:10:00" }, "arrival": { "iataCode": "VIE", "at": "2026-11-20T09:55:00" }, "carrierCode": "TK", "number": "1885", "duration": "PT2H45M", "numberOfStops": 0 }
              ]},
              { "duration": "PT3H05M", "segments": [
                { "departure": { "iataCode": "VIE", "at": "2026-11-24T21:00:00" }, "arrival": { "iataCode": "TBS", "at": "2026-11-25T04:05:00" }, "carrierCode": "OS", "number": "883", "duration": "PT3H05M", "numberOfStops": 0 }
              ]}
            ],
            "price": { "currency": "EUR", "total": "512.40", "grandTotal": "512.40" },
            "validatingAirlineCodes": ["TK"],
            "travelerPricings": [{ "fareDetailsBySegment": [ { "includedCheckedBags": { "quantity": 1 } }, { "includedCheckedBags": { "weight": 23, "weightUnit": "KG" } }, { "includedCheckedBags": { "quantity": 2 } } ] }]
          },
          { "id": "2", "itineraries": [], "price": { "currency": "EUR", "total": "1.00" } }]
        }
        """;
        var response = JsonSerializer.Deserialize<AmadeusFlightOffersResponse>(json)!;

        var offers = new AmadeusFlightOfferMapper(30).Map(response, Request, Now);

        var offer = Assert.Single(offers);
        Assert.Equal(512.40m, offer.TotalPrice);
        Assert.Equal("EUR", offer.Currency);
        Assert.Equal(1, offer.Outbound.Stops);
        Assert.Equal(0, offer.Inbound.Stops);
        Assert.Equal(TimeSpan.FromMinutes(385), offer.Outbound.Duration);
        Assert.Equal(new DateTime(2026, 11, 20, 4, 30, 0), offer.Outbound.DepartureLocal);
        Assert.Equal("VIE", offer.Outbound.ArrivalAirport);
        Assert.Equal(1, offer.IncludedCheckedBags);
        Assert.Null(offer.BookingUrl); // Amadeus does not provide consumer booking links
        Assert.Equal(VerificationStatus.Live, offer.Verification);
        Assert.Equal(Now.AddMinutes(30), offer.ExpiresAtUtc);
        Assert.Contains("TK381", offer.Signature);
    }

    [Fact]
    public void Travelpayouts_Ticket_Is_Labelled_Cached_And_Multiplied_Per_Passenger()
    {
        const string json = """
        { "success": true, "currency": "eur", "data": [
          { "origin": "TBS", "destination": "VIE", "origin_airport": "TBS", "destination_airport": "VIE", "price": 180, "airline": "OS",
            "flight_number": 882, "departure_at": "2026-11-20T05:10:00+04:00", "return_at": "2026-11-24T21:00:00+01:00",
            "transfers": 0, "return_transfers": 1, "duration_to": 245, "duration_back": 400, "link": "/search/TBS2011VIE24111?t=abc" },
          { "origin": "TBS", "destination": "VIE", "price": 90, "departure_at": "2026-11-21T05:10:00+04:00", "return_at": "2026-11-24T21:00:00+01:00", "link": "/x" }
        ]}
        """;
        var response = JsonSerializer.Deserialize<TravelpayoutsPricesResponse>(json)!;
        var mapper = new TravelpayoutsFlightOfferMapper(new TravelpayoutsOptions { LinkBaseUrl = "https://www.aviasales.com", Marker = "12345" });

        var offers = mapper.Map(response, Request, Now);

        var offer = Assert.Single(offers); // second ticket has the wrong departure date
        Assert.Equal(360m, offer.TotalPrice);
        Assert.True(offer.PriceExtrapolatedFromSinglePassenger);
        Assert.Equal(VerificationStatus.Cached, offer.Verification);
        Assert.Null(offer.ExpiresAtUtc);
        Assert.Null(offer.IncludedCheckedBags);
        Assert.Equal(new DateTime(2026, 11, 20, 5, 10, 0), offer.Outbound.DepartureLocal);
        Assert.Equal(1, offer.Inbound.Stops);
        Assert.Equal("https://www.aviasales.com/search/TBS2011VIE24111?t=abc&marker=12345", offer.BookingUrl);
        Assert.Equal("OS882", offer.Outbound.Segments.Single().FlightNumber);
    }

    [Fact]
    public void BookingCom_Search_And_Details_Are_Normalized_Without_Double_Counting_Charges()
    {
        const string search = """
        { "data": [
          { "id": 10001, "currency": { "booker": "EUR" }, "url": "https://www.booking.com/hotel/at/demo.html?aid=1",
            "price": { "book": 400.0, "total": 412.5 },
            "products": [ { "policies": { "cancellation": { "type": "free_cancellation", "free_cancellation_until": "2026-11-18T23:59:00+00:00" }, "meal_plan": { "plan": "breakfast_included" } } } ] },
          { "id": 10002, "currency": { "booker": "EUR" }, "price": { "total": { "booker_currency": 300 } } },
          { "id": 10003, "price": { } }
        ]}
        """;
        const string details = """
        { "data": [ { "id": 10001, "name": { "en-gb": "Hotel Demo Wien" },
            "location": { "address": { "en-gb": "Ringstrasse 1" }, "coordinates": { "latitude": 48.2, "longitude": 16.37 } },
            "rating": { "stars": 4, "review_score": 8.6 } } ] }
        """;
        var request = new AccommodationSearchRequest(Guid.NewGuid(), "Vienna", "AT", -1995499, 48.2, 16.37,
            new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), 2, 1, "EUR", null, 20);

        var detailMap = BookingComMapper.MapDetails(JsonDocument.Parse(details).RootElement);
        var offers = BookingComMapper.MapSearch(JsonDocument.Parse(search).RootElement, detailMap, request, Now, 30);

        Assert.Equal(2, offers.Count);
        var hotel = offers.Single(o => o.PropertyId == "10001");
        Assert.Equal("Hotel Demo Wien", hotel.Name);
        Assert.Equal(412.5m, hotel.TotalPrice);
        Assert.Equal(4m, hotel.Stars);
        Assert.Equal(48.2, hotel.Latitude);
        var fee = Assert.Single(hotel.Fees);
        Assert.True(fee.IncludedInPrice);           // already inside "total"
        Assert.True(fee.PayableAtProperty);
        Assert.Equal(12.5m, fee.Amount);
        Assert.Equal("https://www.booking.com/hotel/at/demo.html?aid=1", hotel.BookingUrl);
        Assert.Contains("Free cancellation", hotel.CancellationPolicy);
        Assert.Equal(300m, offers.Single(o => o.PropertyId == "10002").TotalPrice);
        Assert.Equal("Booking.com property 10002", offers.Single(o => o.PropertyId == "10002").Name);
    }

    [Fact]
    public void Google_Routes_Transit_And_Drive_Are_Estimates_Never_Quotes()
    {
        const string transit = """
        { "routes": [ { "duration": "2100s", "distanceMeters": 21000,
          "travelAdvisory": { "transitFare": { "currencyCode": "EUR", "units": "4", "nanos": 500000000 } },
          "legs": [ { "steps": [ { "transitDetails": { "transitLine": { "nameShort": "S7", "vehicle": { "type": "COMMUTER_TRAIN" } } } }, { } ] } ] } ] }
        """;
        const string drive = """{ "routes": [ { "duration": "1500s", "distanceMeters": 19500 } ] }""";

        var t = GoogleRoutesMapper.MapTransit(JsonDocument.Parse(transit).RootElement)!;
        var d = GoogleRoutesMapper.MapDrive(JsonDocument.Parse(drive).RootElement)!;

        Assert.Equal(4.5m, t.EstimatedCost);
        Assert.True(t.CostIsPerPerson);
        Assert.Equal(VerificationStatus.Estimate, t.CostVerification);
        Assert.Equal(TimeSpan.FromMinutes(35), t.Duration);
        Assert.Equal(["Train S7"], t.Lines);
        Assert.False(t.IsConfirmedQuote);
        Assert.Null(d.EstimatedCost);
        Assert.Equal(VerificationStatus.Unavailable, d.CostVerification);
        Assert.Contains("not a taxi quote", d.Notes);
        Assert.Null(GoogleRoutesMapper.MapTransit(JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public void Ecb_Rates_Parse_And_Cross_Via_Euro()
    {
        const string xml = """
        <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
          <Cube><Cube time="2026-10-19"><Cube currency="USD" rate="1.0800"/><Cube currency="GBP" rate="0.8400"/></Cube></Cube>
        </gesmes:Envelope>
        """;
        var rates = EcbExchangeRateProvider.Parse(xml);

        var usdToEur = EcbExchangeRateProvider.Cross(rates, "USD", "EUR")!;
        var gbpToUsd = EcbExchangeRateProvider.Cross(rates, "GBP", "USD")!;

        Assert.Equal(new DateOnly(2026, 10, 19), rates.Date);
        Assert.Equal(Math.Round(1m / 1.08m, 8), usdToEur.Rate);
        Assert.Equal(Math.Round(1.08m / 0.84m, 8), gbpToUsd.Rate);
        Assert.Contains("2026-10-19", usdToEur.Source);
        Assert.Null(EcbExchangeRateProvider.Cross(rates, "GEL", "EUR")); // not published -> no guessing
    }

    [Fact]
    public void Amadeus_Duration_Parsing()
    {
        Assert.Equal(TimeSpan.FromMinutes(155), AmadeusFlightOfferMapper.ParseDuration("PT2H35M"));
        Assert.Equal(TimeSpan.FromHours(26), AmadeusFlightOfferMapper.ParseDuration("P1DT2H"));
        Assert.Null(AmadeusFlightOfferMapper.ParseDuration("nonsense"));
        Assert.Null(AmadeusFlightOfferMapper.ParseDuration(null));
    }
}
