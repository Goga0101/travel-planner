import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AirportDto,
  ClientConfigDto,
  CreateTripSearchCommand,
  CreateTripSearchResponse,
  DestinationDetailDto,
  DestinationSummaryDto,
  OutboundLinkDto,
  PagedResult,
  PlanSort,
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

  getTripPlan(id: string): Observable<TripPlanDetailDto> {
    return this.http.get<TripPlanDetailDto>(`${BASE}/trip-plans/${encodeURIComponent(id)}`);
  }

  refreshPrices(id: string): Observable<TripPlanDetailDto> {
    return this.http.post<TripPlanDetailDto>(`${BASE}/trip-plans/${encodeURIComponent(id)}/refresh-prices`, {});
  }

  getOutboundLinks(id: string): Observable<OutboundLinkDto[]> {
    return this.http.get<OutboundLinkDto[]>(`${BASE}/trip-plans/${encodeURIComponent(id)}/outbound-links`);
  }

  getClientConfig(): Observable<ClientConfigDto> {
    return this.http.get<ClientConfigDto>(`${BASE}/client-config`);
  }
}
