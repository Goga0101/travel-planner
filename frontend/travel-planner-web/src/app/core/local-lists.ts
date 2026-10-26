import { Injectable, computed, signal } from '@angular/core';
import { TripPlanSummaryDto, TripSearchCriteriaDto } from './models';

// Per-browser lists kept in localStorage. Storage can be unavailable (private mode, blocked site data):
// every access is guarded and the lists then simply live in memory for the session.

function read<T>(key: string): T[] {
  try {
    const raw = localStorage.getItem(key);
    const value: unknown = raw ? JSON.parse(raw) : [];
    return Array.isArray(value) ? (value as T[]) : [];
  } catch {
    return [];
  }
}

function write<T>(key: string, items: T[]): void {
  try {
    localStorage.setItem(key, JSON.stringify(items));
  } catch {
    /* storage unavailable – keep the in-memory list */
  }
}

export interface SavedTrip {
  id: string;
  code: string;
  city: string;
  country: string;
  departureDate: string;
  returnDate: string;
  nights: number;
  travelers: number;
  total: number;
  currency: string;
  savedAt: string;
}

@Injectable({ providedIn: 'root' })
export class SavedTripsService {
  static readonly KEY = 'tp.savedTrips';
  private readonly state = signal<SavedTrip[]>(read<SavedTrip>(SavedTripsService.KEY));

  readonly trips = this.state.asReadonly();
  readonly count = computed(() => this.state().length);

  isSaved(id: string): boolean {
    return this.state().some((t) => t.id === id);
  }

  toggle(s: TripPlanSummaryDto): boolean {
    if (this.isSaved(s.id)) {
      this.remove(s.id);
      return false;
    }
    const trip: SavedTrip = {
      id: s.id,
      code: s.destination.code,
      city: s.destination.city,
      country: s.destination.country,
      departureDate: s.departureDate,
      returnDate: s.returnDate,
      nights: s.nights,
      travelers: s.travelers,
      total: s.totalEstimatedCost,
      currency: s.currency,
      savedAt: new Date().toISOString(),
    };
    this.update([trip, ...this.state()].slice(0, 50));
    return true;
  }

  remove(id: string): void {
    this.update(this.state().filter((t) => t.id !== id));
  }

  private update(items: SavedTrip[]): void {
    this.state.set(items);
    write(SavedTripsService.KEY, items);
  }
}

export interface RecentSearch {
  id: string;
  origin: string;
  destinations: string[];
  earliestDeparture: string;
  latestReturn: string;
  durationDays: number;
  travelers: number;
  budgetAmount: number;
  budgetCurrency: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class RecentSearchesService {
  static readonly KEY = 'tp.recentSearches';
  private readonly state = signal<RecentSearch[]>(read<RecentSearch>(RecentSearchesService.KEY));

  readonly searches = this.state.asReadonly();

  add(id: string, c: Omit<TripSearchCriteriaDto, 'nights' | 'destinationMode' | 'destinations' | 'preferences'>, destinations: string[]): void {
    const entry: RecentSearch = {
      id,
      origin: c.originAirportIata,
      destinations,
      earliestDeparture: c.earliestDeparture,
      latestReturn: c.latestReturn,
      durationDays: c.durationDays,
      travelers: c.travelers,
      budgetAmount: c.budgetAmount,
      budgetCurrency: c.budgetCurrency,
      createdAt: new Date().toISOString(),
    };
    const items = [entry, ...this.state().filter((s) => s.id !== id)].slice(0, 4);
    this.state.set(items);
    write(RecentSearchesService.KEY, items);
  }

  clear(): void {
    this.state.set([]);
    write(RecentSearchesService.KEY, []);
  }
}
