using System.Globalization;

namespace TravelPlanner.Application.Common;

/// <summary>
/// Server-generated, user-facing texts in the supported languages (English default, Georgian).
/// The language comes from <see cref="CultureInfo.CurrentUICulture"/>: set per request from Accept-Language,
/// and per background search from the language the search was created in.
/// </summary>
public static class T
{
    public const string English = "en";
    public const string Georgian = "ka";
    public static readonly string[] SupportedLanguages = [English, Georgian];

    public static string CurrentLanguage => Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    public static string Normalize(string? language) =>
        string.Equals(language?.Split('-')[0], Georgian, StringComparison.OrdinalIgnoreCase) ? Georgian : English;

    /// <summary>Culture used for formatting dates/numbers inside generated texts.</summary>
    public static CultureInfo CultureFor(string language) =>
        Normalize(language) == Georgian ? CultureInfo.GetCultureInfo("ka-GE") : CultureInfo.GetCultureInfo("en-GB");

    /// <summary>Runs <paramref name="action"/> with the given language as current (UI) culture.</summary>
    public static IDisposable Use(string language)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        var culture = CultureFor(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return new Restore(previous);
    }

    public static string Get(string key, params object?[] args)
    {
        var table = CurrentLanguage == Georgian ? Ka : En;
        var template = table.TryGetValue(key, out var text) ? text : En.TryGetValue(key, out var fallback) ? fallback : key;
        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, args);
    }

    /// <summary>"1 stop" / "2 stops" style helper.</summary>
    public static string Stops(int stops) => stops switch
    {
        0 => Get("flight.direct"),
        1 => Get("flight.oneStop"),
        _ => Get("flight.stops", stops)
    };

    /// <summary>Keys defined for a language (used to verify both languages are complete).</summary>
    public static IReadOnlyCollection<string> KeysFor(string language) => (Normalize(language) == Georgian ? Ka : En).Keys;

    private sealed class Restore((CultureInfo Culture, CultureInfo UiCulture) previous) : IDisposable
    {
        public void Dispose()
        {
            CultureInfo.CurrentCulture = previous.Culture;
            CultureInfo.CurrentUICulture = previous.UiCulture;
        }
    }

    internal static readonly Dictionary<string, string> En = new()
    {
        // Search progress / lifecycle
        ["progress.queued"] = "Queued",
        ["progress.started"] = "Search started",
        ["progress.flightsStart"] = "Searching flights to {0} destination(s) on {1} date option(s)",
        ["progress.flights"] = "Searching flights ({0}/{1})",
        ["progress.accommodationStart"] = "Searching accommodation ({0} request(s))",
        ["progress.accommodation"] = "Searching accommodation ({0}/{1})",
        ["progress.transfers"] = "Checking airport transfers",
        ["progress.ranking"] = "Ranking plans",
        ["progress.completed"] = "Search completed",
        ["progress.noMatch"] = "No plans fit the budget",
        ["progress.failed"] = "Search failed",
        ["progress.cancelled"] = "Search cancelled",

        // Planning errors and warnings
        ["error.noFlightProvider"] = "No flight data provider is configured. Configure a flight provider or enable demo mode (see README).",
        ["error.noDates"] = "No valid departure/return dates fit the selected window and trip duration.",
        ["error.noDestinations"] = "None of the candidate destinations has an airport reachable from the selected departure airport.",
        ["error.unknownOrigin"] = "Unknown departure airport '{0}'.",
        ["error.unexpected"] = "The search failed because of an unexpected error. Please try again later.",
        ["error.interrupted"] = "The search was interrupted by a server restart. Please search again.",
        ["warn.destinationLimit"] = "Request limit: only {0} of {1} destinations were searched.",
        ["warn.dateLimit"] = "Request limit: {0} of {1} possible departure dates were checked per destination (evenly spread across the window).",
        ["warn.noFlights"] = "No round-trip flights matching your preferences were found for the selected destinations and dates.",
        ["warn.flightConversion"] = "Some flight prices could not be converted to {0} and were ignored.",
        ["warn.flightsOverBudget"] = "Every flight found costs more than the total budget.",
        ["warn.noAccommodationProvider"] = "No accommodation provider is configured; accommodation costs are missing from all plans.",
        ["warn.dailyNotConfigured"] = "Daily expense estimates were requested but are not configured on this server (Budget section).",
        ["warn.noPlanFits"] = "No plan fits the budget once all known costs are included. The closest options are shown as over budget.",
        ["warn.noRate"] = "No exchange rate {0}->{1} available; '{2}' is excluded from the total.",
        ["reason.flightsExceedBudget"] = "Accommodation was not searched because flights alone exceed the budget.",
        ["reason.noAccommodationProvider"] = "No accommodation provider is configured.",
        ["reason.noAccommodationMatch"] = "No available accommodation matched your criteria.",

        // Provider failure aggregation
        ["area.Flights"] = "Flights",
        ["area.Accommodation"] = "Accommodation",
        ["area.Transfers"] = "Transfers",
        ["provider.notConfigured"] = "{0}: provider '{1}' is not configured.",
        ["provider.rateLimited"] = "{0}: provider '{1}' rate-limited {2} request(s); some results may be missing.",
        ["provider.timeout"] = "{0}: provider '{1}' timed out on {2} request(s); some results may be missing.",
        ["provider.failed"] = "{0}: provider '{1}' failed on {2} request(s) ({3}); some results may be missing.",

        // Plan items
        ["flight.title"] = "Round trip {0} ⇄ {1}",
        ["flight.summary"] = "Out {0:ddd dd MMM HH:mm} ({1}), back {2:ddd dd MMM HH:mm} ({3})",
        ["flight.direct"] = "direct",
        ["flight.oneStop"] = "1 stop",
        ["flight.stops"] = "{0} stops",
        ["stay.summary"] = "{0} night(s), {1:dd MMM} – {2:dd MMM}",
        ["stay.feeIncluded"] = "{0} (included in accommodation price)",
        ["stay.payableAtProperty"] = "Payable at the property",
        ["stay.unavailableTitle"] = "Accommodation not available",
        ["stay.unavailableSummary"] = "{0} night(s) needed. {1}",
        ["stay.searchLinkTitle"] = "Find accommodation on {0}",
        ["stay.searchLinkSummary"] = "{0} night(s) needed. {1} Prices are shown on {2} and are not included in the total.",
        ["transfer.toAccommodation"] = "Airport → accommodation",
        ["transfer.toAirport"] = "Accommodation → airport",
        ["transfer.title"] = "{0} ({1})",
        ["transfer.summaryCost"] = "{0}: est. {1:0.00} {2}",
        ["transfer.summaryMinutes"] = ", ~{0} min",
        ["transfer.summaryNoCost"] = "{0}: ~{1} min, cost unavailable",
        ["transfer.seeOfficial"] = "See official transport information",
        ["transfer.none"] = "No transfer information available",
        ["transfer.disclaimer"] = "Route times and costs are estimates, not taxi quotes. Confirm prices with the operator before travelling.",
        ["transfer.taxiTariffTitle"] = "Taxi (tariff-based estimate)",
        ["transfer.taxiTariffProvider"] = "Configured tariff",
        ["transfer.taxiTariffNotes"] = "Calculated from the configured tariff and driving distance. Not a quote.",
        ["transfer.airportInfoTitle"] = "{0} – official ground transport information",
        ["transfer.airportInfoNotes"] = "Official airport website: taxis, buses and trains.",
        ["transfer.publicTransport"] = "Public transport",
        ["transfer.byRoad"] = "By road (taxi or car)",
        ["transfer.transitFareNotes"] = "Route and fare from Google Maps transit data at search time; fares and timetables can differ on the travel date.",
        ["transfer.transitNoFareNotes"] = "Route from Google Maps transit data; fare not provided.",
        ["transfer.driveNotes"] = "Driving distance and time estimate only. This is not a taxi quote.",
        ["vehicle.Train"] = "Train",
        ["vehicle.Metro"] = "Metro",
        ["vehicle.Bus"] = "Bus",
        ["vehicle.Tram"] = "Tram",
        ["vehicle.Ferry"] = "Ferry",
        ["estimate.provider"] = "Configured estimate",
        ["estimate.food"] = "Food & drinks (estimate)",
        ["estimate.localTransport"] = "Local transport (estimate)",
        ["estimate.dailySummary"] = "{0:0.00} {1} × {2} day(s) × {3} traveller(s)",
        ["estimate.dailyBasis"] = "Operator-configured daily allowance",
        ["estimate.contingencyTitle"] = "Contingency reserve ({0:0.##}%)",
        ["estimate.contingencyBasis"] = "{0:0.##}% of known costs",

        // Provider notes
        ["note.amadeus"] = "Live fare from Amadeus Flight Offers Search (total for all passengers, including taxes). Amadeus does not provide a consumer booking link: book with the operating airline or a travel agency.",
        ["note.baggageUnknown"] = "Checked baggage allowance not reported by the provider.",
        ["note.travelpayouts"] = "Cached price indication from the Aviasales Data API (prices recently found by Aviasales users). The current price and availability must be confirmed on Aviasales.",
        ["note.travelpayoutsBaggage"] = "Baggage allowance not included in cached price data; check on Aviasales.",
        ["booking.propertyFallback"] = "Booking.com property {0}",
        ["booking.chargesAtProperty"] = "Taxes and charges payable at the property",
        ["booking.freeCancellation"] = "Free cancellation until {0:yyyy-MM-dd HH:mm} UTC",
        ["demo.flightNote"] = "DEMO DATA – generated for demonstration only. Not a real flight or price.",
        ["demo.address"] = "Demo address, {0:0.0} km from {1} centre",
        ["demo.categoryBudget"] = "Hostel / budget",
        ["demo.categoryApartment"] = "Apartment",
        ["demo.categoryHotel"] = "Hotel",
        ["demo.cityTax"] = "City tax (demo)",
        ["demo.vat"] = "VAT (demo)",
        ["demo.freeCancellation"] = "Free cancellation until 2 days before arrival (demo)",
        ["demo.nonRefundable"] = "Non-refundable (demo)",
        ["demo.breakfast"] = "Breakfast included (demo)",
        ["demo.publicTransport"] = "Public transport (demo)",
        ["demo.taxi"] = "Taxi (demo estimate)",
        ["demo.airportExpress"] = "Demo airport express",
        ["demo.routeNote"] = "DEMO DATA – not a real route or fare.",
        ["demo.taxiNote"] = "DEMO DATA – not a taxi quote.",

        // Provider status (client-config)
        ["status.demo"] = "Demo mode: generated, clearly labelled sample data.",
        ["status.amadeusOn"] = "Live fares (Enterprise credentials).",
        ["status.amadeusOff"] = "Not configured. Self-Service was shut down on 2026-07-17; Enterprise credentials are required.",
        ["status.travelpayoutsOn"] = "Cached price indications with Aviasales links.",
        ["status.travelpayoutsOff"] = "Not configured (needs a Travelpayouts API token).",
        ["status.bookingOn"] = "Live availability and prices.",
        ["status.bookingOff"] = "Not configured (requires Booking.com Managed Affiliate Partner approval).",
        ["status.googleOn"] = "Transit and driving route estimates.",
        ["status.googleOff"] = "Not configured. Official airport and transit links are still shown.",
        ["status.demoProvider"] = "Demo data (not real)",

        // Refresh prices
        ["refresh.flightProviderMissing"] = "Flight price could not be refreshed: provider '{0}' is not configured on this server.",
        ["refresh.flightFailed"] = "Flight price could not be refreshed ({0}). The previous price is shown with its original timestamp.",
        ["refresh.flightReplaced"] = "The original flight is no longer offered by {0}; it was replaced with the cheapest equivalent.",
        ["refresh.flightGoneSummary"] = "This flight is no longer available.",
        ["refresh.flightGone"] = "The flight is no longer available for these dates.",
        ["refresh.stayProviderMissing"] = "Accommodation price could not be refreshed: provider '{0}' is not configured on this server.",
        ["refresh.stayFailed"] = "Accommodation price could not be refreshed ({0}). The previous price is shown with its original timestamp.",
        ["refresh.stayGoneSummary"] = "This property is no longer available for these dates.",
        ["refresh.stayGone"] = "The accommodation is no longer available for these dates. Run a new search for alternatives.",

        // Links
        ["link.book"] = "Book: {0}",
        ["link.directions"] = "Directions: {0}",

        // API errors
        ["api.validationTitle"] = "One or more validation errors occurred.",
        ["api.notFoundTitle"] = "Not found",
        ["api.notFound"] = "{0} '{1}' was not found.",
        ["api.conflictTitle"] = "Conflict",
        ["api.badRequest"] = "Bad request",
        ["api.unexpectedTitle"] = "An unexpected error occurred.",
        ["api.tryLater"] = "Please try again later.",
        ["api.tooManyTitle"] = "Too many requests",
        ["api.tooMany"] = "Please wait before trying again.",
        ["api.alreadyFinished"] = "The search has already finished with status '{0}'.",
        ["resource.tripSearch"] = "Trip search",
        ["resource.tripPlan"] = "Trip plan",
        ["resource.destination"] = "Destination",

        // Validation
        ["val.originRequired"] = "Departure airport is required.",
        ["val.originFormat"] = "Departure airport must be a 3-letter IATA code.",
        ["val.originUnsupported"] = "Departure airport is not supported.",
        ["val.duration"] = "Trip duration must be between {0} and {1} days.",
        ["val.departureFuture"] = "Earliest departure must be in the future.",
        ["val.departureWithin"] = "Earliest departure must be within {0} days.",
        ["val.returnAfter"] = "Latest return must be after the earliest departure.",
        ["val.windowMax"] = "The travel window may span at most {0} days.",
        ["val.windowShort"] = "The travel window is shorter than the trip duration.",
        ["val.travelers"] = "Travelers must be between {0} and {1}.",
        ["val.budgetPositive"] = "Budget must be greater than 0.",
        ["val.budgetMax"] = "Budget must not exceed {0:N0}.",
        ["val.budgetPrecision"] = "Budget can have at most 2 decimal places.",
        ["val.currencyRequired"] = "Currency is required.",
        ["val.currency"] = "Currency must be one of: {0}.",
        ["val.mode"] = "Destination mode is invalid.",
        ["val.manualRequired"] = "Select at least one destination in manual mode.",
        ["val.manualMax"] = "Select at most {0} destinations.",
        ["val.manualUnknown"] = "One or more selected destinations do not exist or are inactive.",
        ["val.automaticNoSelection"] = "Destinations must not be selected in automatic mode.",
        ["val.stars"] = "Minimum stars must be between 1 and 5.",
        ["val.flightHours"] = "Maximum flight duration must be between 1 and 48 hours.",
        ["val.maxStops"] = "Maximum stops must be between 0 and 3.",
        ["val.departureTime"] = "Preferred departure time is invalid.",
        ["val.page"] = "Page must be 1 or greater.",
        ["val.pageSize"] = "Page size must be between 1 and {0}.",
        ["val.sort"] = "Sort order is invalid.",
        ["val.searchLength"] = "Search text must be at most 100 characters.",
        ["val.countryCode"] = "Country code must be 2 letters."
    };

    internal static readonly Dictionary<string, string> Ka = new()
    {
        ["progress.queued"] = "რიგშია",
        ["progress.started"] = "ძიება დაიწყო",
        ["progress.flightsStart"] = "ვეძებთ ფრენებს {0} მიმართულებით, {1} თარიღის ვარიანტზე",
        ["progress.flights"] = "ვეძებთ ფრენებს ({0}/{1})",
        ["progress.accommodationStart"] = "ვეძებთ განთავსებას ({0} მოთხოვნა)",
        ["progress.accommodation"] = "ვეძებთ განთავსებას ({0}/{1})",
        ["progress.transfers"] = "ვამოწმებთ აეროპორტის ტრანსფერებს",
        ["progress.ranking"] = "ვალაგებთ გეგმებს",
        ["progress.completed"] = "ძიება დასრულდა",
        ["progress.noMatch"] = "ბიუჯეტში ჯდომადი გეგმა ვერ მოიძებნა",
        ["progress.failed"] = "ძიება ვერ შესრულდა",
        ["progress.cancelled"] = "ძიება გაუქმდა",

        ["error.noFlightProvider"] = "ფრენების მონაცემთა პროვაიდერი არ არის კონფიგურირებული. დააკონფიგურირეთ პროვაიდერი ან ჩართეთ დემო რეჟიმი (იხ. README).",
        ["error.noDates"] = "არჩეულ პერიოდსა და მოგზაურობის ხანგრძლივობას არცერთი გაფრენა-დაბრუნების თარიღი არ ერგება.",
        ["error.noDestinations"] = "არცერთ მიმართულებას არ აქვს აეროპორტი, რომელიც არჩეული გაფრენის აეროპორტიდან ხელმისაწვდომია.",
        ["error.unknownOrigin"] = "გაფრენის აეროპორტი „{0}“ უცნობია.",
        ["error.unexpected"] = "ძიება მოულოდნელი შეცდომის გამო ვერ შესრულდა. გთხოვთ, მოგვიანებით სცადოთ.",
        ["error.interrupted"] = "ძიება სერვერის გადატვირთვის გამო შეწყდა. გთხოვთ, თავიდან მოძებნოთ.",
        ["warn.destinationLimit"] = "მოთხოვნების ლიმიტი: მოიძებნა {1}-დან მხოლოდ {0} მიმართულება.",
        ["warn.dateLimit"] = "მოთხოვნების ლიმიტი: თითოეული მიმართულებისთვის შემოწმდა გაფრენის {1} შესაძლო თარიღიდან {0} (თანაბრად განაწილებული პერიოდში).",
        ["warn.noFlights"] = "არჩეული მიმართულებებისა და თარიღებისთვის თქვენს პარამეტრებს შესაბამისი ორმხრივი ფრენა ვერ მოიძებნა.",
        ["warn.flightConversion"] = "ზოგიერთი ფრენის ფასის {0}-ში კონვერტაცია ვერ მოხერხდა და ისინი არ იქნა გათვალისწინებული.",
        ["warn.flightsOverBudget"] = "ნაპოვნი ყველა ფრენა მთლიან ბიუჯეტზე ძვირია.",
        ["warn.noAccommodationProvider"] = "განთავსების პროვაიდერი არ არის კონფიგურირებული; განთავსების ხარჯი არცერთ გეგმაში არ არის.",
        ["warn.dailyNotConfigured"] = "მოთხოვნილია დღიური ხარჯების შეფასება, მაგრამ ის ამ სერვერზე არ არის კონფიგურირებული (Budget სექცია).",
        ["warn.noPlanFits"] = "ყველა ცნობილი ხარჯის გათვალისწინებით, ბიუჯეტში არცერთი გეგმა არ ჯდება. ყველაზე ახლო ვარიანტები ნაჩვენებია, როგორც ბიუჯეტის გადამეტება.",
        ["warn.noRate"] = "გაცვლითი კურსი {0}->{1} მიუწვდომელია; „{2}“ ჯამში არ არის ჩათვლილი.",
        ["reason.flightsExceedBudget"] = "განთავსება არ მოიძებნა, რადგან მხოლოდ ფრენები აჭარბებს ბიუჯეტს.",
        ["reason.noAccommodationProvider"] = "განთავსების პროვაიდერი არ არის კონფიგურირებული.",
        ["reason.noAccommodationMatch"] = "თქვენს კრიტერიუმებს თავისუფალი განთავსება არ შეესაბამება.",

        ["area.Flights"] = "ფრენები",
        ["area.Accommodation"] = "განთავსება",
        ["area.Transfers"] = "ტრანსფერები",
        ["provider.notConfigured"] = "{0}: პროვაიდერი „{1}“ არ არის კონფიგურირებული.",
        ["provider.rateLimited"] = "{0}: პროვაიდერმა „{1}“ შეზღუდა {2} მოთხოვნა; ზოგიერთი შედეგი შეიძლება აკლდეს.",
        ["provider.timeout"] = "{0}: პროვაიდერმა „{1}“ დროულად ვერ უპასუხა {2} მოთხოვნას; ზოგიერთი შედეგი შეიძლება აკლდეს.",
        ["provider.failed"] = "{0}: პროვაიდერთან „{1}“ {2} მოთხოვნა ვერ შესრულდა ({3}); ზოგიერთი შედეგი შეიძლება აკლდეს.",

        ["flight.title"] = "ორმხრივი ფრენა {0} ⇄ {1}",
        ["flight.summary"] = "გაფრენა {0:ddd, dd MMM HH:mm} ({1}), დაბრუნება {2:ddd, dd MMM HH:mm} ({3})",
        ["flight.direct"] = "პირდაპირი",
        ["flight.oneStop"] = "1 გადაჯდომა",
        ["flight.stops"] = "{0} გადაჯდომა",
        ["stay.summary"] = "{0} ღამე, {1:dd MMM} – {2:dd MMM}",
        ["stay.feeIncluded"] = "{0} (შედის განთავსების ფასში)",
        ["stay.payableAtProperty"] = "გადაიხდება ადგილზე",
        ["stay.unavailableTitle"] = "განთავსება მიუწვდომელია",
        ["stay.unavailableSummary"] = "საჭიროა {0} ღამე. {1}",
        ["stay.searchLinkTitle"] = "მოძებნეთ განთავსება {0}-ზე",
        ["stay.searchLinkSummary"] = "საჭიროა {0} ღამე. {1} ფასები ნაჩვენებია {2}-ზე და ჯამში არ შედის.",
        ["transfer.toAccommodation"] = "აეროპორტი → განთავსება",
        ["transfer.toAirport"] = "განთავსება → აეროპორტი",
        ["transfer.title"] = "{0} ({1})",
        ["transfer.summaryCost"] = "{0}: დაახლ. {1:0.00} {2}",
        ["transfer.summaryMinutes"] = ", ~{0} წთ",
        ["transfer.summaryNoCost"] = "{0}: ~{1} წთ, ღირებულება უცნობია",
        ["transfer.seeOfficial"] = "იხილეთ ოფიციალური სატრანსპორტო ინფორმაცია",
        ["transfer.none"] = "ტრანსფერის შესახებ ინფორმაცია არ არის",
        ["transfer.disclaimer"] = "მარშრუტის დრო და ღირებულება სავარაუდოა და ტაქსის შეთავაზება არ არის. მგზავრობამდე ფასი გადაამოწმეთ გადამზიდთან.",
        ["transfer.taxiTariffTitle"] = "ტაქსი (ტარიფით გამოთვლილი)",
        ["transfer.taxiTariffProvider"] = "კონფიგურირებული ტარიფი",
        ["transfer.taxiTariffNotes"] = "გამოთვლილია კონფიგურირებული ტარიფითა და გზის მანძილით. ეს შეთავაზება არ არის.",
        ["transfer.airportInfoTitle"] = "{0} – ოფიციალური ინფორმაცია ტრანსპორტზე",
        ["transfer.airportInfoNotes"] = "აეროპორტის ოფიციალური ვებგვერდი: ტაქსები, ავტობუსები და მატარებლები.",
        ["transfer.publicTransport"] = "საზოგადოებრივი ტრანსპორტი",
        ["transfer.byRoad"] = "მანქანით (ტაქსი ან ავტომობილი)",
        ["transfer.transitFareNotes"] = "მარშრუტი და ტარიფი Google Maps-ის მონაცემებით ძიების მომენტში; მგზავრობის დღეს ტარიფი და განრიგი შეიძლება განსხვავდებოდეს.",
        ["transfer.transitNoFareNotes"] = "მარშრუტი Google Maps-ის მონაცემებით; ტარიფი მითითებული არ არის.",
        ["transfer.driveNotes"] = "მხოლოდ გზის მანძილი და სავარაუდო დრო. ეს ტაქსის შეთავაზება არ არის.",
        ["vehicle.Train"] = "მატარებელი",
        ["vehicle.Metro"] = "მეტრო",
        ["vehicle.Bus"] = "ავტობუსი",
        ["vehicle.Tram"] = "ტრამვაი",
        ["vehicle.Ferry"] = "ბორანი",
        ["estimate.provider"] = "კონფიგურირებული შეფასება",
        ["estimate.food"] = "კვება და სასმელი (სავარაუდო)",
        ["estimate.localTransport"] = "ადგილობრივი ტრანსპორტი (სავარაუდო)",
        ["estimate.dailySummary"] = "{0:0.00} {1} × {2} დღე × {3} მგზავრი",
        ["estimate.dailyBasis"] = "ოპერატორის მიერ განსაზღვრული დღიური თანხა",
        ["estimate.contingencyTitle"] = "სარეზერვო თანხა ({0:0.##}%)",
        ["estimate.contingencyBasis"] = "ცნობილი ხარჯების {0:0.##}%",

        ["note.amadeus"] = "რეალური ტარიფი Amadeus Flight Offers Search-იდან (ჯამი ყველა მგზავრზე, გადასახადების ჩათვლით). Amadeus დაჯავშნის ბმულს არ იძლევა: დაჯავშნეთ ავიაკომპანიასთან ან ტურისტულ სააგენტოში.",
        ["note.baggageUnknown"] = "ჩაბარებული ბარგის ნორმა პროვაიდერს არ მიუთითებია.",
        ["note.travelpayouts"] = "შენახული ფასი Aviasales Data API-დან (ფასები, რომლებიც Aviasales-ის მომხმარებლებმა ახლახან ნახეს). მიმდინარე ფასი და ხელმისაწვდომობა გადაამოწმეთ Aviasales-ზე.",
        ["note.travelpayoutsBaggage"] = "შენახულ ფასში ბარგის ნორმა არ არის მითითებული; გადაამოწმეთ Aviasales-ზე.",
        ["booking.propertyFallback"] = "Booking.com-ის ობიექტი {0}",
        ["booking.chargesAtProperty"] = "ადგილზე გადასახდელი გადასახადები და მოსაკრებლები",
        ["booking.freeCancellation"] = "უფასო გაუქმება {0:yyyy-MM-dd HH:mm} UTC-მდე",
        ["demo.flightNote"] = "დემო მონაცემები – შექმნილია მხოლოდ საჩვენებლად. ეს არ არის რეალური ფრენა ან ფასი.",
        ["demo.address"] = "დემო მისამართი, {1}-ის ცენტრიდან {0:0.0} კმ",
        ["demo.categoryBudget"] = "ჰოსტელი / ეკონომი",
        ["demo.categoryApartment"] = "აპარტამენტი",
        ["demo.categoryHotel"] = "სასტუმრო",
        ["demo.cityTax"] = "ქალაქის გადასახადი (დემო)",
        ["demo.vat"] = "დღგ (დემო)",
        ["demo.freeCancellation"] = "უფასო გაუქმება ჩამოსვლამდე 2 დღით ადრე (დემო)",
        ["demo.nonRefundable"] = "თანხა არ ბრუნდება (დემო)",
        ["demo.breakfast"] = "საუზმე შედის (დემო)",
        ["demo.publicTransport"] = "საზოგადოებრივი ტრანსპორტი (დემო)",
        ["demo.taxi"] = "ტაქსი (დემო შეფასება)",
        ["demo.airportExpress"] = "დემო აეროპორტის ექსპრესი",
        ["demo.routeNote"] = "დემო მონაცემები – არ არის რეალური მარშრუტი ან ტარიფი.",
        ["demo.taxiNote"] = "დემო მონაცემები – ტაქსის შეთავაზება არ არის.",

        ["status.demo"] = "დემო რეჟიმი: გენერირებული, მკაფიოდ მონიშნული სანიმუშო მონაცემები.",
        ["status.amadeusOn"] = "რეალური ტარიფები (Enterprise წვდომა).",
        ["status.amadeusOff"] = "არ არის კონფიგურირებული. Self-Service 2026-07-17-ს დაიხურა; საჭიროა Enterprise წვდომა.",
        ["status.travelpayoutsOn"] = "შენახული ფასები Aviasales-ის ბმულებით.",
        ["status.travelpayoutsOff"] = "არ არის კონფიგურირებული (საჭიროა Travelpayouts API ტოკენი).",
        ["status.bookingOn"] = "რეალური ხელმისაწვდომობა და ფასები.",
        ["status.bookingOff"] = "არ არის კონფიგურირებული (საჭიროა Booking.com-ის პარტნიორობის დამტკიცება).",
        ["status.googleOn"] = "საზოგადოებრივი ტრანსპორტისა და გზის მარშრუტების შეფასება.",
        ["status.googleOff"] = "არ არის კონფიგურირებული. აეროპორტისა და ტრანსპორტის ოფიციალური ბმულები მაინც ჩანს.",
        ["status.demoProvider"] = "დემო მონაცემები (არარეალური)",

        ["refresh.flightProviderMissing"] = "ფრენის ფასი ვერ განახლდა: პროვაიდერი „{0}“ ამ სერვერზე არ არის კონფიგურირებული.",
        ["refresh.flightFailed"] = "ფრენის ფასი ვერ განახლდა ({0}). ნაჩვენებია წინა ფასი თავდაპირველი დროით.",
        ["refresh.flightReplaced"] = "{0} თავდაპირველ ფრენას აღარ გვთავაზობს; ის შეიცვალა ყველაზე იაფი ანალოგით.",
        ["refresh.flightGoneSummary"] = "ეს ფრენა აღარ არის ხელმისაწვდომი.",
        ["refresh.flightGone"] = "ამ თარიღებზე ფრენა აღარ არის ხელმისაწვდომი.",
        ["refresh.stayProviderMissing"] = "განთავსების ფასი ვერ განახლდა: პროვაიდერი „{0}“ ამ სერვერზე არ არის კონფიგურირებული.",
        ["refresh.stayFailed"] = "განთავსების ფასი ვერ განახლდა ({0}). ნაჩვენებია წინა ფასი თავდაპირველი დროით.",
        ["refresh.stayGoneSummary"] = "ეს ობიექტი ამ თარიღებზე აღარ არის ხელმისაწვდომი.",
        ["refresh.stayGone"] = "განთავსება ამ თარიღებზე აღარ არის ხელმისაწვდომი. ალტერნატივისთვის ახალი ძიება ჩაატარეთ.",

        ["link.book"] = "დაჯავშნა: {0}",
        ["link.directions"] = "მარშრუტი: {0}",

        ["api.validationTitle"] = "მონაცემები შეიცავს შეცდომებს.",
        ["api.notFoundTitle"] = "ვერ მოიძებნა",
        ["api.notFound"] = "{0} „{1}“ ვერ მოიძებნა.",
        ["api.conflictTitle"] = "კონფლიქტი",
        ["api.badRequest"] = "არასწორი მოთხოვნა",
        ["api.unexpectedTitle"] = "მოულოდნელი შეცდომა.",
        ["api.tryLater"] = "გთხოვთ, მოგვიანებით სცადოთ.",
        ["api.tooManyTitle"] = "ძალიან ბევრი მოთხოვნა",
        ["api.tooMany"] = "გთხოვთ, ცოტა ხანს მოიცადოთ და თავიდან სცადოთ.",
        ["api.alreadyFinished"] = "ძიება უკვე დასრულებულია სტატუსით „{0}“.",
        ["resource.tripSearch"] = "ძიება",
        ["resource.tripPlan"] = "გეგმა",
        ["resource.destination"] = "მიმართულება",

        ["val.originRequired"] = "მიუთითეთ გაფრენის აეროპორტი.",
        ["val.originFormat"] = "გაფრენის აეროპორტი უნდა იყოს 3-ასოიანი IATA კოდი.",
        ["val.originUnsupported"] = "ეს გაფრენის აეროპორტი არ არის მხარდაჭერილი.",
        ["val.duration"] = "მოგზაურობის ხანგრძლივობა უნდა იყოს {0}-დან {1} დღემდე.",
        ["val.departureFuture"] = "ყველაზე ადრეული გაფრენა მომავალში უნდა იყოს.",
        ["val.departureWithin"] = "ყველაზე ადრეული გაფრენა უნდა იყოს მომდევნო {0} დღის განმავლობაში.",
        ["val.returnAfter"] = "ყველაზე გვიანი დაბრუნება ყველაზე ადრეული გაფრენის შემდეგ უნდა იყოს.",
        ["val.windowMax"] = "მოგზაურობის პერიოდი მაქსიმუმ {0} დღე შეიძლება იყოს.",
        ["val.windowShort"] = "მოგზაურობის პერიოდი მოგზაურობის ხანგრძლივობაზე მოკლეა.",
        ["val.travelers"] = "მგზავრების რაოდენობა უნდა იყოს {0}-დან {1}-მდე.",
        ["val.budgetPositive"] = "ბიუჯეტი 0-ზე მეტი უნდა იყოს.",
        ["val.budgetMax"] = "ბიუჯეტი არ უნდა აღემატებოდეს {0:N0}-ს.",
        ["val.budgetPrecision"] = "ბიუჯეტს შეიძლება ჰქონდეს მაქსიმუმ 2 ათწილადი ნიშანი.",
        ["val.currencyRequired"] = "მიუთითეთ ვალუტა.",
        ["val.currency"] = "ვალუტა უნდა იყოს ერთ-ერთი: {0}.",
        ["val.mode"] = "მიმართულების არჩევის რეჟიმი არასწორია.",
        ["val.manualRequired"] = "ხელით არჩევის რეჟიმში აირჩიეთ მინიმუმ ერთი მიმართულება.",
        ["val.manualMax"] = "აირჩიეთ მაქსიმუმ {0} მიმართულება.",
        ["val.manualUnknown"] = "ერთი ან მეტი არჩეული მიმართულება არ არსებობს ან არააქტიურია.",
        ["val.automaticNoSelection"] = "ავტომატურ რეჟიმში მიმართულებები არ უნდა აირჩიოთ.",
        ["val.stars"] = "ვარსკვლავების მინიმუმი უნდა იყოს 1-დან 5-მდე.",
        ["val.flightHours"] = "ფრენის მაქსიმალური ხანგრძლივობა უნდა იყოს 1-დან 48 საათამდე.",
        ["val.maxStops"] = "გადაჯდომების მაქსიმუმი უნდა იყოს 0-დან 3-მდე.",
        ["val.departureTime"] = "გაფრენის სასურველი დრო არასწორია.",
        ["val.page"] = "გვერდის ნომერი 1 ან მეტი უნდა იყოს.",
        ["val.pageSize"] = "გვერდის ზომა უნდა იყოს 1-დან {0}-მდე.",
        ["val.sort"] = "დალაგების წესი არასწორია.",
        ["val.searchLength"] = "საძიებო ტექსტი მაქსიმუმ 100 სიმბოლო შეიძლება იყოს.",
        ["val.countryCode"] = "ქვეყნის კოდი 2 ასოსგან უნდა შედგებოდეს."
    };
}
