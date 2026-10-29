// Mirrors the backend DTOs (camelCase JSON, enums as strings, nulls omitted => optional).

export type TripSearchStatus = 'Queued' | 'Running' | 'Completed' | 'CompletedNoMatch' | 'Failed' | 'Cancelled';
export type DestinationMode = 'Automatic' | 'Manual';
export type DataMode = 'Live' | 'Demo';
export type PlanItemType = 'Flight' | 'Accommodation' | 'Transfer' | 'Fee' | 'DailyExpenseEstimate' | 'Contingency';
export type TransferDirection = 'None' | 'AirportToAccommodation' | 'AccommodationToAirport';
export type VerificationStatus = 'Live' | 'Cached' | 'Estimate' | 'Demo' | 'Unavailable' | 'Verified';
export type BudgetFitStatus = 'WithinBudget' | 'WithinBudgetIncomplete' | 'OverBudget' | 'Unknown';
export type DepartureTimePreference = 'Any' | 'Morning' | 'Afternoon' | 'Evening' | 'Night';
export type TransportLinkKind = 'AirportInformation' | 'PublicTransport' | 'TaxiOperator' | 'RideHailingPlatform';
export type TransferMode = 'PublicTransport' | 'Taxi' | 'Drive' | 'RideHailing' | 'Information';
export type PlanSort = 'Rank' | 'TotalCost' | 'TotalCostDesc' | 'RemainingBudget' | 'DepartureDate';
export type Continent = 'Europe' | 'Asia' | 'Africa' | 'NorthAmerica' | 'SouthAmerica' | 'Oceania';
export const CONTINENTS: Continent[] = ['Europe', 'Asia', 'Africa', 'NorthAmerica', 'SouthAmerica', 'Oceania'];

/** .NET TimeSpan as serialized by System.Text.Json: "hh:mm:ss[.fffffff]" or "d.hh:mm:ss[.fffffff]". */
export type TimeSpanString = string;

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

export interface AirportDto {
  id: string;
  iataCode: string;
  name: string;
  city: string;
  countryCode: string;
  latitude: number;
  longitude: number;
  isDepartureOption: boolean;
  officialWebsiteUrl?: string;
  cityCode?: string;
  country?: string;
  continent?: Continent;
}

/** A city anywhere in the world; destinationId is set when it is a catalog destination. */
export interface PlaceDto {
  cityCode: string;
  city: string;
  country: string;
  countryCode: string;
  continent: Continent;
  airportCodes: string[];
  destinationId?: string;
  /** A town without an airport: distance to the nearest of airportCodes (its gateways). */
  gatewayKm?: number;
}

/** Cheapest known one-way fare of a day; price per passenger, time airport-local (HH:mm). */
export interface FlightCalendarDayDto {
  date: string;
  time: string;
  from: string;
  to: string;
  stops: number;
  airline: string | null;
  price: number;
  bookingUrl: string | null;
}

/** One route of the fare calendar; fromKm/toKm are set when that end is a nearby city instead of the one asked. */
export interface FlightCalendarRouteDto {
  fromCode: string;
  fromCity: string;
  toCode: string;
  toCity: string;
  isRequested: boolean;
  fromKm: number | null;
  toKm: number | null;
  days: FlightCalendarDayDto[];
}

/** OneWay, RoundTrip (one ticket both ways), TwoOneWays (a ticket each way), SelfTransfer (a change on separate tickets). */
export type FlightOptionKind = 'OneWay' | 'RoundTrip' | 'TwoOneWays' | 'SelfTransfer';

/** A flight for the chosen dates; price is for all travellers, tickets are the separately booked ones (empty for one ticket). */
export interface FlightOptionDto {
  kind: FlightOptionKind;
  price: number;
  currency: string;
  outbound: FlightLegSummaryDto;
  inbound: FlightLegSummaryDto | null;
  bookingUrl: string | null;
  tickets: FlightTicket[];
}

export interface FlightOptionsDto {
  available: boolean;
  currency: string;
  adults: number;
  options: FlightOptionDto[];
  /** A live search for the same dates on the fare site. */
  searchUrl: string | null;
}

export interface FlightCalendarDto {
  available: boolean;
  currency: string;
  routes: FlightCalendarRouteDto[];
}

export interface DestinationSummaryDto {
  id: string;
  code: string;
  city: string;
  country: string;
  countryCode: string;
  region: string;
  airportCodes: string[];
  popularity: number;
  description?: string;
}

export interface TransportLinkDto {
  id: string;
  kind: TransportLinkKind;
  name: string;
  url?: string;
  phone?: string;
  notes?: string;
  isVerified: boolean;
  verifiedOn?: string;
}

export interface DestinationDetailDto {
  id: string;
  code: string;
  city: string;
  country: string;
  countryCode: string;
  region: string;
  description?: string;
  latitude: number;
  longitude: number;
  isActive: boolean;
  popularity: number;
  hasTaxiTariff: boolean;
  airports: AirportDto[];
  transportLinks: TransportLinkDto[];
}

export interface TripPreferencesDto {
  includeCheckedBaggage: boolean;
  minAccommodationStars?: number | null;
  maxFlightDurationHours?: number | null;
  maxStops?: number | null;
  preferredDepartureTime: DepartureTimePreference;
  includeDailyExpenseEstimates: boolean;
  allowSelfTransfer?: boolean;
}

export interface CreateTripSearchCommand {
  originAirportIata: string;
  earliestDeparture: string; // yyyy-MM-dd
  latestReturn: string;
  durationDays: number;
  travelers: number;
  budgetAmount: number;
  budgetCurrency: string;
  destinationMode: DestinationMode;
  destinationIds?: string[] | null;
  /** Manual mode: cities outside the catalog (IATA city codes). */
  destinationCityCodes?: string[] | null;
  /** Automatic mode: only these continents (empty = all). */
  continents?: Continent[] | null;
  preferences?: TripPreferencesDto | null;
}

export interface CreateTripSearchResponse {
  id: string;
  status: TripSearchStatus;
  dataMode: DataMode;
}

export interface DestinationRefDto {
  id: string;
  code: string;
  city: string;
  country: string;
  countryCode: string;
}

export interface TripSearchCriteriaDto {
  originAirportIata: string;
  earliestDeparture: string;
  latestReturn: string;
  durationDays: number;
  nights: number;
  travelers: number;
  budgetAmount: number;
  budgetCurrency: string;
  destinationMode: DestinationMode;
  destinations: DestinationRefDto[];
  preferences: TripPreferencesDto;
  continents: Continent[];
}

export interface TripSearchDto {
  id: string;
  status: TripSearchStatus;
  isFinished: boolean;
  progressPercent: number;
  progressMessage: string;
  errorMessage?: string;
  warnings: string[];
  dataMode: DataMode;
  criteria: TripSearchCriteriaDto;
  planCount: number;
  createdAtUtc: string;
  startedAtUtc?: string;
  completedAtUtc?: string;
}

export interface FlightLegSummaryDto {
  from: string;
  to: string;
  departureLocal: string;
  arrivalLocal?: string;
  durationMinutes?: number;
  stops: number;
  carriers: string[];
  fromCity?: string;
  toCity?: string;
  /** Connection airports in order, with their city in the request language. */
  via: ViaStopDto[];
}

export interface ViaStopDto {
  iata: string;
  city: string;
  countryCode?: string;
}

/** One separately booked ticket of a combined offer (price for all passengers). */
export interface FlightTicket {
  isReturn: boolean;
  from: string;
  to: string;
  departureLocal: string;
  arrivalLocal?: string;
  stops: number;
  carrier?: string;
  price: number;
  currency: string;
  bookingUrl?: string;
}

/** A flight found by a search, across all destinations and dates. */
export interface FoundFlightDto {
  destination: DestinationRefDto;
  departureDate: string;
  returnDate: string;
  price: number;
  currency: string;
  originalPrice: number;
  originalCurrency: string;
  withinBudget: boolean;
  provider: string;
  verification: VerificationStatus;
  outbound: FlightLegSummaryDto;
  inbound: FlightLegSummaryDto;
  bookingUrl?: string;
  retrievedAtUtc: string;
  selfTransfer: boolean;
  tickets: FlightTicket[];
}

export interface FlightSummaryDto {
  provider: string;
  price?: number;
  currency?: string;
  verification: VerificationStatus;
  outbound?: FlightLegSummaryDto;
  inbound?: FlightLegSummaryDto;
  includedCheckedBags?: number;
  hasBookingLink: boolean;
  /** Changes planes on separate tickets in at least one direction. */
  selfTransfer: boolean;
}

export interface AccommodationSummaryDto {
  name: string;
  provider: string;
  price?: number;
  currency?: string;
  stars?: number;
  reviewScore?: number;
  address?: string;
  verification: VerificationStatus;
  hasBookingLink: boolean;
  cancellationPolicy?: string;
}

export interface TransferSummaryDto {
  selectedOption?: string;
  durationMinutes?: number;
  estimatedCost?: number;
  currency?: string;
  verification: VerificationStatus;
}

export interface SourceVerificationDto {
  type: PlanItemType;
  direction: TransferDirection;
  provider: string;
  verification: VerificationStatus;
  retrievedAtUtc: string;
  expiresAtUtc?: string;
  isExpired: boolean;
}

export interface TripPlanSummaryDto {
  id: string;
  tripSearchId: string;
  rank: number;
  score: number;
  scoreBreakdown: Record<string, number>;
  destination: DestinationRefDto;
  departureDate: string;
  returnDate: string;
  nights: number;
  travelers: number;
  currency: string;
  budgetAmount: number;
  totalEstimatedCost: number;
  remainingBudget: number;
  budgetFit: BudgetFitStatus;
  missingCostCategories: string[];
  warnings: string[];
  isDemo: boolean;
  hasExpiredItems: boolean;
  earliestExpiryUtc?: string;
  pricesRefreshedAtUtc?: string;
  flight?: FlightSummaryDto;
  accommodation?: AccommodationSummaryDto;
  transfer?: TransferSummaryDto;
  sources: SourceVerificationDto[];
}

// ---- Day planner ----

export type Interest = 'culture' | 'museums' | 'food' | 'cafes' | 'bars' | 'nightlife' | 'nature' | 'shopping' | 'shows';
export const INTERESTS: Interest[] = ['culture', 'museums', 'food', 'cafes', 'bars', 'nightlife', 'nature', 'shopping', 'shows'];
export type TripPace = 'Relaxed' | 'Normal' | 'Intense';
export type PlanDayKind = 'Arrival' | 'Full' | 'Departure';

/** Distance and directions are from the previous stop of the day (the city centre for the first one). */
export interface ActivityPlaceDto {
  id: string;
  name: string;
  interest: Interest;
  kind: string;
  latitude: number;
  longitude: number;
  address?: string;
  openingHours?: string;
  website?: string;
  cuisine?: string;
  imageUrl?: string;
  summary?: string;
  notable: boolean;
  distanceMeters: number;
  mapUrl?: string;
  directionsUrl?: string;
}

export interface CityEventDto {
  id: string;
  name: string;
  date: string;
  time?: string;
  venue?: string;
  segment?: string;
  genre?: string;
  imageUrl?: string;
  priceMin?: number;
  priceMax?: number;
  currency?: string;
  url?: string;
  directionsUrl?: string;
  subGenre?: string;
  latitude?: number;
  longitude?: number;
}

export interface MusicGenreDto {
  name: string;
  count: number;
}

export interface DayItemDto {
  slot: string;
  time: string;
  place?: ActivityPlaceDto;
  event?: CityEventDto;
  alternatives: ActivityPlaceDto[];
}

export interface PlanDayDto {
  number: number;
  date: string;
  kind: PlanDayKind;
  items: DayItemDto[];
}

export interface DayPlanDto {
  interests: Interest[];
  pace: TripPace;
  days: PlanDayDto[];
  events: CityEventDto[];
  eventsConfigured: boolean;
  eventsProvider?: string;
  eventsSearchUrl: string;
  arrivalTime?: string;
  departureTime?: string;
  /** Music genres among the city's events (with counts), the ones kept, and the city's Resident Advisor page. */
  musicGenres: MusicGenreDto[];
  music: string[];
  residentAdvisorUrl?: string;
  /** Where each day starts and ends: the chosen hotel, else the city centre. */
  startLatitude: number;
  startLongitude: number;
}

// ---- Item details (normalized JSON stored per plan item) ----

export interface FlightSegment {
  carrierCode: string;
  flightNumber: string;
  departureAirport: string;
  arrivalAirport: string;
  departureLocal: string;
  arrivalLocal?: string;
  duration?: TimeSpanString;
}

export interface FlightItinerary {
  departureAirport: string;
  arrivalAirport: string;
  departureLocal: string;
  arrivalLocal?: string;
  duration?: TimeSpanString;
  stops: number;
  segments: FlightSegment[];
  via?: string[];
}

export interface FlightItemDetails {
  outbound: FlightItinerary;
  inbound: FlightItinerary;
  passengers: number;
  includedCheckedBags?: number;
  baggageNote?: string;
  priceExtrapolatedFromSinglePassenger: boolean;
  validatingCarrier?: string;
  sourceNote?: string;
  tickets?: FlightTicket[];
}

export interface MandatoryFee {
  name: string;
  amount: number;
  currency: string;
  includedInPrice: boolean;
  payableAtProperty: boolean;
}

export interface AccommodationItemDetails {
  name: string;
  address?: string;
  latitude?: number;
  longitude?: number;
  stars?: number;
  reviewScore?: number;
  category?: string;
  checkIn: string;
  checkOut: string;
  nights: number;
  guests: number;
  rooms: number;
  cancellationPolicy?: string;
  freeCancellationUntil?: string;
  mealPlan?: string;
  fees: MandatoryFee[];
}

export interface FeeItemDetails {
  name: string;
  includedInPrice: boolean;
  payableAtProperty: boolean;
  relatedTo: string;
}

export interface TransferOption {
  mode: TransferMode;
  title: string;
  provider: string;
  distanceMeters?: number;
  duration?: TimeSpanString;
  estimatedCost?: number;
  costCurrency?: string;
  costIsPerPerson: boolean;
  costVerification: VerificationStatus;
  infoVerification: VerificationStatus;
  isConfirmedQuote: boolean;
  url?: string;
  phone?: string;
  notes?: string;
  lines: string[];
}

export interface TransferItemDetails {
  airportIata: string;
  airportName: string;
  airportLatitude: number;
  airportLongitude: number;
  accommodationLatitude?: number;
  accommodationLongitude?: number;
  selectedOptionTitle?: string;
  mapsDirectionsUrl?: string;
  options: TransferOption[];
  disclaimer: string;
}

export interface EstimateItemDetails {
  basis: string;
  unitAmount?: number;
  days?: number;
  travelers?: number;
  percent?: number;
}

export interface PlanItemDto {
  id: string;
  type: PlanItemType;
  direction: TransferDirection;
  category: string;
  title: string;
  summary?: string;
  provider: string;
  price?: number;
  currency?: string;
  convertedPrice?: number;
  exchangeRate?: number;
  exchangeRateSource?: string;
  exchangeRateAsOfUtc?: string;
  includedInTotal: boolean;
  isRequired: boolean;
  bookingUrl?: string;
  retrievedAtUtc: string;
  expiresAtUtc?: string;
  isExpired: boolean;
  verification: VerificationStatus;
  details: unknown;
}

export interface CostBreakdownLineDto {
  category: string;
  amount: number;
}

export interface TripPlanDetailDto {
  summary: TripPlanSummaryDto;
  destination: DestinationDetailDto;
  costBreakdown: CostBreakdownLineDto[];
  items: PlanItemDto[];
}

export interface OutboundLinkDto {
  itemId: string;
  type: PlanItemType;
  direction: TransferDirection;
  label: string;
  provider: string;
  url: string;
  verification: VerificationStatus;
  kind: 'Booking' | 'Information' | 'Map' | string;
}

export interface ProviderStatus {
  category: string;
  name: string;
  active: boolean;
  isDemo: boolean;
  note: string;
}

export interface ClientConfigDto {
  dataMode: DataMode;
  providers: ProviderStatus[];
  supportedCurrencies: string[];
  minDurationDays: number;
  maxDurationDays: number;
  maxTravelers: number;
  maxSearchWindowDays: number;
  maxManualDestinations: number;
  mapsEmbedApiKey?: string;
}

// Typed accessors for item details (the backend guarantees the shape per item type).
export const flightDetails = (i: PlanItemDto) => i.details as FlightItemDetails;
export const accommodationDetails = (i: PlanItemDto) => i.details as AccommodationItemDetails;
export const transferDetails = (i: PlanItemDto) => i.details as TransferItemDetails;
export const feeDetails = (i: PlanItemDto) => i.details as FeeItemDetails;
export const estimateDetails = (i: PlanItemDto) => i.details as EstimateItemDetails;

// ---- Nearby places (OpenStreetMap snapshot + pre-filled outbound links) ----

export interface NearbyAirportDto {
  iataCode: string;
  name: string;
  latitude: number;
  longitude: number;
  officialWebsiteUrl?: string;
}

export interface NearbyHotelDto {
  id: string;
  name: string;
  kind: string;
  stars?: number;
  latitude: number;
  longitude: number;
  distanceFromAirportMeters: number;
  distanceFromCentreMeters: number;
  address?: string;
  phone?: string;
  website?: string;
  bookingUrl?: string;
  mapUrl?: string;
  transitDirectionsUrl?: string;
  uberUrl?: string;
  nearestStop?: HotelStopDto;
  /** Lines that stop both at the airport and next to the hotel: no change needed. */
  directLines: string[];
}

export interface HotelStopDto {
  name: string;
  mode: TransitMode;
  lines: string[];
  distanceMeters: number;
}

export type TransitMode = 'bus' | 'trolleybus' | 'train' | 'subway' | 'tram' | 'light_rail' | 'monorail';

export interface TransitLineDto {
  mode: TransitMode;
  ref?: string;
  name?: string;
  from?: string;
  to?: string;
  operator?: string;
  network?: string;
  website?: string;
}

export interface TransitStopDto {
  name: string;
  mode: TransitMode;
  lines: string[];
  distanceFromAirportMeters: number;
  mapUrl?: string;
}

export interface TaxiRankDto {
  name?: string;
  operator?: string;
  phone?: string;
  distanceFromAirportMeters: number;
  mapUrl?: string;
}

export interface TaxiCompanyDto {
  name: string;
  phone?: string;
  website?: string;
}

export interface NearbyPlacesDto {
  attribution: string;
  retrievedOn?: string;
  airport?: NearbyAirportDto;
  airportHotels: NearbyHotelDto[];
  centreHotels: NearbyHotelDto[];
  transitLines: TransitLineDto[];
  transitStops: TransitStopDto[];
  taxiRanks: TaxiRankDto[];
  taxiCompanies: TaxiCompanyDto[];
  centreTransitDirectionsUrl?: string;
  centreUberUrl?: string;
  hotelSearchUrl?: string;
}

// ---- Indicative hotel prices (price-comparison feed; never part of a plan total) ----

/** url books with that agency: Booking.com directly, the others through the hotel's TripAdvisor page (viaTripAdvisor). */
export interface AgencyRateDto {
  code: string;
  name: string;
  nightly: number;
  url?: string;
  viaTripAdvisor: boolean;
}

export interface HotelPriceDto {
  name: string;
  rating?: number;
  reviewCount?: number;
  distanceFromCentreMeters?: number;
  bookingComNightly?: number;
  lowestNightly: number;
  lowestAgency: string;
  stayTotal: number;
  rates: AgencyRateDto[];
  bookingUrl?: string;
  mapUrl?: string;
  transitDirectionsUrl?: string;
  /** The hotel's TripAdvisor page, listing every agency's offer. */
  compareUrl?: string;
  latitude?: number;
  longitude?: number;
}

// ---- Airport ↔ stay routes and ticket guide ----

export interface TransitLegDto {
  mode: string;
  line?: string;
  headsign?: string;
  from: string;
  to: string;
  departureTime: string;
  arrivalTime: string;
  durationMinutes: number;
  agency?: string;
  stops?: number;
  color?: string;
  ticket?: LegTicketDto;
  /** Where the leg starts and ends and the way it follows (encoded polyline with the given precision). */
  path?: LegPath;
  /** A minibus or bus without a timetable that would only leave in the morning (none at night). */
  waitsForMorning?: boolean;
}

export interface LegPath {
  fromLatitude: number;
  fromLongitude: number;
  toLatitude: number;
  toLongitude: number;
  shape?: string;
  precision: number;
}

/** How to pay for a ride: the matching airport ticket (or the city's ticket) and whether it can be bought online. */
export interface LegTicketDto {
  name?: string;
  price?: number;
  currency?: string;
  buyOnline?: boolean;
  buyUrl?: string;
  note?: string;
  infoUrl?: string;
  cityTicket: boolean;
}

export interface TransitItineraryDto {
  durationMinutes: number;
  transfers: number;
  departureTime: string;
  arrivalTime: string;
  legs: TransitLegDto[];
  /** Built from the lines listed at nearby stops (no published timetable): times are rough. */
  estimated?: boolean;
}

export interface RouteDirectionDto {
  fromName: string;
  toName: string;
  timetableDate: string;
  onTravelDate: boolean;
  wishedTime: string;
  itineraries: TransitItineraryDto[];
  googleTransitUrl?: string;
  googleDriveUrl?: string;
  uberUrl?: string;
  /** The city's normal ticket (on day hops), also when no timetable is known. */
  cityTicket?: LegTicketDto;
}

export interface GuideOptionDto {
  mode: string;
  name: string;
  from?: string;
  to?: string;
  ticket?: string;
  price?: number;
  currency?: string;
  whereToBuy?: string;
  payment?: string;
  duration?: string;
  frequency?: string;
  url?: string;
  buyOnline?: boolean;
  buyUrl?: string;
  buyNote?: string;
}

export interface GuideFareDto {
  name?: string;
  price?: number;
  currency?: string;
}

export interface TransitGuideDto {
  airport: string;
  options: GuideOptionDto[];
  cityTicket?: GuideFareDto;
  dayPass?: GuideFareDto;
  contactless?: boolean;
  apps: { name: string; url?: string }[];
  tips: string[];
  sources: string[];
  checkedOn?: string;
  cityBuyOnline?: boolean;
  cityBuyUrl?: string;
  cityBuyNote?: string;
}

export interface StayRouteDto {
  airportCode?: string;
  airportName?: string;
  stayName: string;
  stayIsCentre: boolean;
  toStay?: RouteDirectionDto;
  toAirport?: RouteDirectionDto;
  guide?: TransitGuideDto;
  routerName?: string;
}

export interface HotelPricesDto {
  source: string;
  currency: string;
  retrievedAtUtc: string;
  nights: number;
  rooms: number;
  adults: number;
  bookingComNightlyMin?: number;
  bookingComNightlyMax?: number;
  lowestNightly: number;
  typicalNightly: number;
  estimatedStayFrom: number;
  estimatedStayTypical: number;
  estimatedTripTotal: number;
  hotels: HotelPriceDto[];
}

// ---- Sights (OpenStreetMap + Wikidata ranking + Wikipedia summaries) ----

export type AttractionKind = 'museum' | 'castle' | 'worship' | 'park' | 'monument' | 'heritage' | 'viewpoint' | 'sight';

export interface AttractionStopDto {
  name: string;
  mode: TransitMode;
  lines: string[];
  distanceMeters: number;
}

export interface AttractionDto {
  id: string;
  name: string;
  kind: AttractionKind;
  latitude: number;
  longitude: number;
  distanceFromCentreMeters: number;
  wikipediaEditions: number;
  shortDescription?: string;
  summary?: string;
  summaryLanguage?: string;
  imageUrl?: string;
  wikipediaUrl?: string;
  website?: string;
  openingHours?: string;
  fee?: string;
  stops: AttractionStopDto[];
  mapUrl?: string;
  directionsUrl?: string;
}

/** A hotel found by name around the destination (the visitor's own find), with a Booking.com link for the trip dates. */
export interface HotelSearchResultDto {
  id: string;
  name: string;
  address?: string;
  kind: string;
  latitude: number;
  longitude: number;
  distanceFromCentreMeters: number;
  bookingUrl?: string;
  mapUrl?: string;
}

// ---------- Live transit (any place → any place) ----------

export type PlaceKind = 'address' | 'stop' | 'city' | 'place' | 'region' | 'airport';

export interface PlaceSuggestionDto {
  name: string;
  area?: string;
  countryCode?: string;
  latitude: number;
  longitude: number;
  kind: PlaceKind;
}

export interface JourneyDto {
  fromName: string;
  toName: string;
  /** IANA zone of the origin; journey times are local there. */
  timeZone?: string;
  countryCode?: string;
  distanceMeters: number;
  direction: RouteDirectionDto;
  routerName?: string;
  /** Intercity minibus stations for trips to another region (no open timetables); the best match first. */
  coaches: CoachStationDto[];
  /** Rough estimates from the distance, so every trip says something; walking only when it is not too far. */
  drivingMinutes: number;
  walkingMinutes?: number;
  /** Set for distances people usually fly (a Google Flights search). */
  flightsUrl?: string;
}

export interface CoachStationDto {
  name: string;
  towns: string[];
  recommended: boolean;
  mapUrl: string;
  directionsUrl: string;
  latitude: number;
  longitude: number;
  /** Public transport from the start to the station (the recommended one). */
  route?: RouteDirectionDto;
}

/** Stop-to-stop stretch: Unix seconds, next stop, encoded polyline (precision 5). */
export interface VehicleStretchDto {
  departure: number;
  arrival: number;
  to: string;
  shape: string;
}

export interface VehicleDto {
  id: string;
  mode: string;
  line?: string;
  color?: string;
  realTime: boolean;
  stretches: VehicleStretchDto[];
}

export interface LiveVehiclesDto {
  serverTime: number;
  tooLarge: boolean;
  vehicles: VehicleDto[];
  source?: string;
}

export interface TaxiAppDto {
  name: string;
  url: string;
  rideUrl?: string;
}

export interface LocalTaxiDto {
  name?: string;
  operator?: string;
  phone?: string;
  website?: string;
  distanceMeters: number;
  mapUrl: string;
  isCompany: boolean;
}

export interface TaxiOptionsDto {
  countryCode?: string;
  apps: TaxiAppDto[];
  local: LocalTaxiDto[];
  searchUrl: string;
  source: string;
}
