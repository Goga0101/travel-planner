import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  AirportDto,
  ClientConfigDto,
  CreateTripSearchCommand,
  CreateTripSearchResponse,
  DestinationDetailDto,
  AttractionDto,
  DayPlanDto,
  DestinationSummaryDto,
  FlightCalendarDto,
  FlightOptionsDto,
  FoundFlightDto,
  HotelPricesDto,
  NearbyPlacesDto,
  OutboundLinkDto,
  PagedResult,
  Interest,
  PlaceDto,
  PlanSort,
  RouteDirectionDto,
  HotelSearchResultDto,
  StayRouteDto,
  TripPace,
  TripPlanDetailDto,
  TripPlanSummaryDto,
  TripSearchDto,
  PlaceSuggestionDto,
  JourneyDto,
  LiveVehiclesDto,
  TaxiOptionsDto,
} from './models';

const BASE = '/api/v1';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  createTripSearch(command: CreateTripSearchCommand): Observable<CreateTripSearchResponse> {
    return this.http.post<CreateTripSearchResponse>(`${BASE}/trip-searches`, command);
  }

  getTripSearch(id: string): Observable<TripSearchDto> {
    return this.http.get<TripSearchDto>(`${BASE}/trip-searches/${encodeURIComponent(id)}`);
  }

  getPlans(id: string, page = 1, pageSize = 20, sort: PlanSort = 'Rank'): Observable<PagedResult<TripPlanSummaryDto>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize).set('sort', sort);
    return this.http.get<PagedResult<TripPlanSummaryDto>>(`${BASE}/trip-searches/${encodeURIComponent(id)}/plans`, { params });
  }

  cancelTripSearch(id: string): Observable<TripSearchDto> {
    return this.http.post<TripSearchDto>(`${BASE}/trip-searches/${encodeURIComponent(id)}/cancel`, {});
  }

  getDestinations(search?: string, page = 1, pageSize = 100): Observable<PagedResult<DestinationSummaryDto>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) params = params.set('search', search);
    return this.http.get<PagedResult<DestinationSummaryDto>>(`${BASE}/destinations`, { params });
  }

  getDestination(id: string): Observable<DestinationDetailDto> {
    return this.http.get<DestinationDetailDto>(`${BASE}/destinations/${encodeURIComponent(id)}`);
  }

  getDepartureAirports(): Observable<AirportDto[]> {
    return this.http.get<AirportDto[]>(`${BASE}/airports`, { params: { departureOnly: true } });
  }

  /** Departure airports worldwide matching a city, airport name or IATA code (best matches first). */
  searchAirports(term: string): Observable<AirportDto[]> {
    return this.http.get<AirportDto[]>(`${BASE}/airports`, { params: { departureOnly: true, search: term } });
  }

  /** The departure airport nearest to the visitor (by IP address); null when unknown (HTTP 204). */
  getNearestAirport(): Observable<AirportDto | null> {
    return this.http
      .get<AirportDto>(`${BASE}/airports/nearest`, { observe: 'response' })
      .pipe(map((r) => (r.status === 204 ? null : r.body)));
  }

  /** Cities anywhere in the world (with an airport), for destinations outside the catalog. */
  searchPlaces(term: string): Observable<PlaceDto[]> {
    return this.http.get<PlaceDto[]>(`${BASE}/places`, { params: { search: term } });
  }

  /** Cheapest known one-way fare of each day over the coming months, for the route and for nearby airports. */
  getFlightCalendar(origin: string, destination: string, currency: string, nearby = true): Observable<FlightCalendarDto> {
    return this.http.get<FlightCalendarDto>(`${BASE}/flights/calendar`, { params: { origin, destination, currency, nearby } });
  }

  /** Flights for the dates (one way without a return date), cheapest first, with their booking links. */
  getFlightOptions(origin: string, destination: string, departure: string, returnDate: string | null, adults: number, currency: string): Observable<FlightOptionsDto> {
    let params = new HttpParams().set('origin', origin).set('destination', destination).set('departure', departure).set('adults', adults).set('currency', currency);
    if (returnDate) params = params.set('return', returnDate);
    return this.http.get<FlightOptionsDto>(`${BASE}/flights/options`, { params });
  }

  /** Every flight a finished search found (all destinations and dates), cheapest first. */
  getFoundFlights(searchId: string): Observable<FoundFlightDto[]> {
    return this.http.get<FoundFlightDto[]>(`${BASE}/trip-searches/${encodeURIComponent(searchId)}/flights`);
  }

  getTripPlan(id: string): Observable<TripPlanDetailDto> {
    return this.http.get<TripPlanDetailDto>(`${BASE}/trip-plans/${encodeURIComponent(id)}`);
  }

  refreshPrices(id: string): Observable<TripPlanDetailDto> {
    return this.http.post<TripPlanDetailDto>(`${BASE}/trip-plans/${encodeURIComponent(id)}/refresh-prices`, {});
  }

  getOutboundLinks(id: string): Observable<OutboundLinkDto[]> {
    return this.http.get<OutboundLinkDto[]>(`${BASE}/trip-plans/${encodeURIComponent(id)}/outbound-links`);
  }

  getNearbyPlaces(id: string): Observable<NearbyPlacesDto> {
    return this.http.get<NearbyPlacesDto>(`${BASE}/trip-plans/${encodeURIComponent(id)}/nearby`);
  }

  /** Indicative hotel prices for the plan's dates; null when none are available (HTTP 204). */
  /** Hotels around the destination whose name contains the text (the visitor's own find). */
  searchHotels(planId: string, text: string): Observable<HotelSearchResultDto[]> {
    return this.http.get<HotelSearchResultDto[]>(`${BASE}/trip-plans/${encodeURIComponent(planId)}/hotel-search`, { params: new HttpParams().set('q', text) });
  }

  getHotelPrices(id: string): Observable<HotelPricesDto | null> {
    return this.http
      .get<HotelPricesDto>(`${BASE}/trip-plans/${encodeURIComponent(id)}/hotel-prices`, { observe: 'response' })
      .pipe(map((r) => (r.status === 204 ? null : r.body)));
  }

  /** Day-by-day itinerary for the chosen interests and pace, with the city's events during the trip. */
  getDayPlan(planId: string, interests: Interest[], pace: TripPace, music: string[] = [], from?: { latitude: number; longitude: number } | null): Observable<DayPlanDto> {
    let params = new HttpParams().set('interests', interests.join(',')).set('pace', pace);
    if (music.length) params = params.set('music', music.join(','));
    if (from) params = params.set('fromLat', from.latitude).set('fromLon', from.longitude);
    return this.http.get<DayPlanDto>(`${BASE}/trip-plans/${encodeURIComponent(planId)}/day-plan`, { params });
  }

  /** Airport ↔ stay journeys (the chosen hotel, or the city centre when none) and the city's ticket guide. */
  getStayRoute(planId: string, stay: { name: string; latitude: number; longitude: number } | null): Observable<StayRouteDto> {
    let params = new HttpParams();
    if (stay) params = params.set('lat', stay.latitude).set('lon', stay.longitude).set('name', stay.name);
    return this.http.get<StayRouteDto>(`${BASE}/trip-plans/${encodeURIComponent(planId)}/route`, { params });
  }

  /** Public transport between two stops of a planned day, leaving on the date at the local time (HH:mm), with tickets. */
  getHop(planId: string, from: { name: string; latitude: number; longitude: number }, to: { name: string; latitude: number; longitude: number },
    date: string, time: string): Observable<RouteDirectionDto> {
    const params = new HttpParams()
      .set('fromLat', from.latitude).set('fromLon', from.longitude).set('fromName', from.name)
      .set('toLat', to.latitude).set('toLon', to.longitude).set('toName', to.name)
      .set('date', date).set('time', time);
    return this.http.get<RouteDirectionDto>(`${BASE}/trip-plans/${encodeURIComponent(planId)}/hop`, { params });
  }

  getAttractions(destinationId: string): Observable<AttractionDto[]> {
    return this.http.get<AttractionDto[]>(`${BASE}/destinations/${encodeURIComponent(destinationId)}/attractions`);
  }

  getClientConfig(): Observable<ClientConfigDto> {
    return this.http.get<ClientConfigDto>(`${BASE}/client-config`);
  }

  findPlaces(text: string, near?: { latitude: number; longitude: number }): Observable<PlaceSuggestionDto[]> {
    let params = new HttpParams().set('q', text);
    if (near) params = params.set('lat', near.latitude.toFixed(3)).set('lon', near.longitude.toFixed(3));
    return this.http.get<PlaceSuggestionDto[]>(`${BASE}/transit/places`, { params });
  }

  reversePlace(latitude: number, longitude: number): Observable<PlaceSuggestionDto | null> {
    const params = new HttpParams().set('lat', latitude.toFixed(5)).set('lon', longitude.toFixed(5));
    return this.http.get<PlaceSuggestionDto | null>(`${BASE}/transit/reverse`, { params });
  }

  getJourney(from: PlaceSuggestionDto, to: PlaceSuggestionDto, time?: string): Observable<JourneyDto> {
    let params = new HttpParams()
      .set('fromLat', from.latitude.toFixed(6)).set('fromLon', from.longitude.toFixed(6)).set('fromName', from.name)
      .set('toLat', to.latitude.toFixed(6)).set('toLon', to.longitude.toFixed(6)).set('toName', to.name);
    if (time) params = params.set('time', time);
    return this.http.get<JourneyDto>(`${BASE}/transit/journey`, { params });
  }

  getVehicles(minLat: number, minLon: number, maxLat: number, maxLon: number): Observable<LiveVehiclesDto> {
    const params = new HttpParams()
      .set('minLat', minLat.toFixed(3)).set('minLon', minLon.toFixed(3)).set('maxLat', maxLat.toFixed(3)).set('maxLon', maxLon.toFixed(3));
    return this.http.get<LiveVehiclesDto>(`${BASE}/transit/vehicles`, { params });
  }

  getTaxis(at: PlaceSuggestionDto, to?: PlaceSuggestionDto): Observable<TaxiOptionsDto> {
    let params = new HttpParams().set('lat', at.latitude.toFixed(5)).set('lon', at.longitude.toFixed(5));
    if (at.countryCode) params = params.set('country', at.countryCode);
    if (to) params = params.set('toLat', to.latitude.toFixed(5)).set('toLon', to.longitude.toFixed(5));
    return this.http.get<TaxiOptionsDto>(`${BASE}/transit/taxis`, { params });
  }
}
