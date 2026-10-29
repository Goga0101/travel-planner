import { Injectable, Pipe, PipeTransform, inject, signal } from '@angular/core';

/** public/airlines.json, written by scripts/fetch-airlines.mjs. */
interface AirlinesFile {
  names: Record<string, string>;
  lowcost: string[];
}

export interface Airline {
  code: string;
  /** Null while the list loads or for a code it does not know. */
  name: string | null;
  lowcost: boolean;
}

/**
 * Airline names by IATA code ("A9" → "Georgian Airways"). Flight data only carries the code; the list (~25 KB) is fetched
 * once, on the first airline shown, next to index.html so it works under a sub-path such as /travel-planner/.
 */
@Injectable({ providedIn: 'root' })
export class AirlinesService {
  private readonly data = signal<AirlinesFile | null>(null);
  private requested = false;

  /** Reactive: re-evaluates when the list arrives. */
  get(code: string): Airline {
    this.load();
    const key = code.trim().toUpperCase();
    const data = this.data();
    return { code: key, name: data?.names[key] ?? null, lowcost: data?.lowcost.includes(key) ?? false };
  }

  /** "GQ785" → "GQ"; the airline part of a flight number. */
  static codeOf(flightNumber: string): string {
    return /^([A-Z0-9]{2})\s?\d/i.exec(flightNumber.trim())?.[1].toUpperCase() ?? flightNumber;
  }

  private load(): void {
    if (this.requested) return;
    this.requested = true;
    fetch('airlines.json')
      .then((r) => (r.ok ? (r.json() as Promise<AirlinesFile>) : null))
      .then((json) => { if (json?.names) this.data.set({ names: json.names, lowcost: json.lowcost ?? [] }); })
      .catch(() => { /* no list: codes stay codes */ });
  }
}

/** "A9" → "Georgian Airways (A9)": both at once, where there is no room to point at it (inside a tooltip). */
@Pipe({ name: 'airline', pure: false })
export class AirlinePipe implements PipeTransform {
  private readonly airlines = inject(AirlinesService);

  transform(code: string | null | undefined): string {
    if (!code) return '';
    const a = this.airlines.get(code);
    return a.name ? `${a.name} (${a.code})` : a.code;
  }
}
