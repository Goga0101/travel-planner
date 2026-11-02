import { Injectable, signal } from '@angular/core';

/**
 * Indicative price of the picked hotel for the whole stay (all nights and rooms), in the price list's currency.
 * typical = the hotel had no price of its own, so the city's typical hotel price stands in.
 */
export interface StayPrice {
  total: number;
  nightly: number;
  currency: string;
  typical?: boolean;
}

/** The hotel the visitor intends to book for a plan; routes and the day plan start from it. */
export interface SelectedStay {
  name: string;
  latitude: number;
  longitude: number;
  price?: StayPrice;
}

/** The plan total with the picked hotel's estimate added (only when it is in the plan's currency). */
export interface TripTotal {
  /** Known costs from the plan (flights, transfers…). */
  known: number;
  /** The picked hotel's estimate, or null when none can be added. */
  stay: StayPrice | null;
  total: number;
  remaining: number;
}

export function tripTotal(known: number, budget: number, currency: string, stay: SelectedStay | null): TripTotal {
  const price = stay?.price && stay.price.currency === currency && stay.price.total > 0 ? stay.price : null;
  const total = known + (price?.total ?? 0);
  return { known, stay: price, total, remaining: budget - total };
}

const KEY = 'tp.selectedStays';

function read(): Record<string, SelectedStay> {
  try {
    const raw = localStorage.getItem(KEY);
    const value = raw ? (JSON.parse(raw) as Record<string, SelectedStay>) : {};
    return value && typeof value === 'object' ? value : {};
  } catch {
    return {};
  }
}

/** Per-browser choice per plan id; storage may be unavailable, then it lives in memory for the session. */
@Injectable({ providedIn: 'root' })
export class SelectedStayService {
  private readonly state = signal<Record<string, SelectedStay>>(read());

  readonly all = this.state.asReadonly();

  get(planId: string): SelectedStay | null {
    return this.state()[planId] ?? null;
  }

  isSelected(planId: string, latitude: number | null | undefined, longitude: number | null | undefined): boolean {
    const s = this.get(planId);
    return !!s && latitude != null && longitude != null && Math.abs(s.latitude - latitude) < 1e-5 && Math.abs(s.longitude - longitude) < 1e-5;
  }

  /** Selects the hotel, or clears the choice when it is already selected. */
  toggle(planId: string, stay: SelectedStay): void {
    const next = { ...this.state() };
    if (this.isSelected(planId, stay.latitude, stay.longitude)) delete next[planId];
    else next[planId] = stay;
    this.state.set(next);
    try {
      localStorage.setItem(KEY, JSON.stringify(next));
    } catch {
      /* storage unavailable */
    }
  }

  clear(planId: string): void {
    const next = { ...this.state() };
    delete next[planId];
    this.state.set(next);
    try {
      localStorage.setItem(KEY, JSON.stringify(next));
    } catch {
      /* storage unavailable */
    }
  }
}
