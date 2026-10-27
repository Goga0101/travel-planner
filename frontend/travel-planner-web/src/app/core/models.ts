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

export interface AgencyRateDto {
  code: string;
  name: string;
  nightly: number;
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
