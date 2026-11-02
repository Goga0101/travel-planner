import { Injectable, signal } from '@angular/core';
import { HotelPriceDto, HotelPricesDto } from './models';

/**
 * Price of the picked hotel for the whole stay (all nights and rooms), as one booking site publishes it for the dates.
 * typical = the hotel had no price of its own, so the city's typical hotel price stands in.
 */
export interface StayPrice {
  total: number;
  nightly: number;
  currency: string;
  /** The site whose published price this is (Booking.com when it has one). */
  source?: string;
  /** When the price list was fetched. */
  retrievedAtUtc?: string;
  typical?: boolean;
}

const BOOKING_COM = 'BookingCom';

function stayTotalOf(hp: HotelPricesDto, nightly: number): number {
  return Math.round(nightly * hp.nights * Math.max(1, hp.rooms) * 100) / 100;
}

/** The hotel's price on Booking.com when listed there, otherwise on the cheapest site, for the whole stay. */
export function sitePrice(hp: HotelPricesDto, h: HotelPriceDto): StayPrice {
  const rate = h.rates.find((r) => r.code === BOOKING_COM) ?? h.rates.reduce((a, b) => (b.nightly < a.nightly ? b : a), h.rates[0]);
  const nightly = rate?.nightly ?? h.lowestNightly;
  return { total: stayTotalOf(hp, nightly), nightly, currency: hp.currency, source: rate?.name ?? h.lowestAgency, retrievedAtUtc: hp.retrievedAtUtc };
}

/** The city's typical hotel price for the stay, for a hotel without a price of its own. */
export function typicalPrice(hp: HotelPricesDto): StayPrice | undefined {
  return hp.typicalNightly > 0
    ? { total: stayTotalOf(hp, hp.typicalNightly), nightly: hp.typicalNightly, currency: hp.currency, retrievedAtUtc: hp.retrievedAtUtc, typical: true }
    : undefined;
}

/** The picked hotel in a price list: same name, or within about 150 m. */
export function findPriced(hp: HotelPricesDto, stay: SelectedStay): HotelPriceDto | null {
  const name = stay.name.trim().toLowerCase();
  return hp.hotels.find((h) => h.name.trim().toLowerCase() === name)
    ?? hp.hotels.find((h) => h.latitude != null && h.longitude != null
      && Math.abs(h.latitude - stay.latitude) < 0.0014 && Math.abs(h.longitude - stay.longitude) < 0.0018)
    ?? null;
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

  /** Replaces the picked hotel's price (a fresh price list), keeping the choice itself. */
  setPrice(planId: string, price: StayPrice | undefined): void {
    const current = this.get(planId);
    if (!current) return;
    const next = { ...this.state(), [planId]: { ...current, price } };
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
