import { Injectable, signal } from '@angular/core';

/** The hotel the visitor intends to book for a plan; routes and the day plan start from it. */
export interface SelectedStay {
  name: string;
  latitude: number;
  longitude: number;
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
