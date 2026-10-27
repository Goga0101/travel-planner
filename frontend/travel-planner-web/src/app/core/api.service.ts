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
  FoundFlightDto,
  HotelPricesDto,
  NearbyPlacesDto,
  OutboundLinkDto,
  PagedResult,
  Interest,
  PlaceDto,
  PlanSort,
  TripPace,
  TripPlanDetailDto,
  TripPlanSummaryDto,
  TripSearchDto,
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
  getHotelPrices(id: string): Observable<HotelPricesDto | null> {
    return this.http
      .get<HotelPricesDto>(`${BASE}/trip-plans/${encodeURIComponent(id)}/hotel-prices`, { observe: 'response' })
      .pipe(map((r) => (r.status === 204 ? null : r.body)));
  }

  /** Day-by-day itinerary for the chosen interests and pace, with the city's events during the trip. */
  getDayPlan(planId: string, interests: Interest[], pace: TripPace): Observable<DayPlanDto> {
    const params = new HttpParams().set('interests', interests.join(',')).set('pace', pace);
    return this.http.get<DayPlanDto>(`${BASE}/trip-plans/${encodeURIComponent(planId)}/day-plan`, { params });
  }

  getAttractions(destinationId: string): Observable<AttractionDto[]> {
    return this.http.get<AttractionDto[]>(`${BASE}/destinations/${encodeURIComponent(destinationId)}/attractions`);
  }

  getClientConfig(): Observable<ClientConfigDto> {
    return this.http.get<ClientConfigDto>(`${BASE}/client-config`);
  }
}
