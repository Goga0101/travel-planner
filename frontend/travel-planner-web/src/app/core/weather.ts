import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

/** Open-Meteo serves a daily forecast for today + 15 days; anything later uses last year's observations as a reference. */
export const FORECAST_DAYS = 16;

export type WeatherKind = 'forecast' | 'lastYear';
export type WeatherIcon = 'sun' | 'cloud' | 'rain' | 'snow';

export interface WeatherDay {
  date: string;
  icon: WeatherIcon;
  maxC: number;
  minC: number;
  /** Forecast: probability of precipitation (%). Last year: measured precipitation (mm). */
  precipitation: number | null;
}

export interface WeatherOutlook {
  kind: WeatherKind;
  days: WeatherDay[];
}

interface OpenMeteoDaily {
  daily?: {
    time: string[];
    weather_code: (number | null)[];
    temperature_2m_max: (number | null)[];
    temperature_2m_min: (number | null)[];
    precipitation_probability_max?: (number | null)[];
    precipitation_sum?: (number | null)[];
  };
}

/** WMO weather interpretation code → one of four icons. */
export function weatherIcon(code: number | null | undefined): WeatherIcon {
  if (code === null || code === undefined) return 'cloud';
  if (code <= 1) return 'sun';
  if ((code >= 71 && code <= 77) || code === 85 || code === 86) return 'snow';
  if (code >= 51) return 'rain';
  return 'cloud';
}

function shiftYear(iso: string, years: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  // 29 February has no counterpart in a non-leap year: use 28 February.
  const day = m === 2 && d === 29 ? 28 : d;
  return `${String(y + years).padStart(4, '0')}-${String(m).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

function daysBetween(fromIso: string, toIso: string): number {
  return Math.round((Date.parse(toIso) - Date.parse(fromIso)) / 86_400_000);
}

/** Builds the Open-Meteo request for a trip: a forecast when the whole trip is within range, otherwise last year's archive. */
export function weatherRequest(lat: number, lon: number, startIso: string, endIso: string, todayIso: string): { kind: WeatherKind; url: string } {
  const common = `latitude=${lat.toFixed(4)}&longitude=${lon.toFixed(4)}&timezone=auto`;
  if (daysBetween(todayIso, endIso) < FORECAST_DAYS && daysBetween(todayIso, startIso) >= 0) {
    return {
      kind: 'forecast',
      url: `https://api.open-meteo.com/v1/forecast?${common}&start_date=${startIso}&end_date=${endIso}` +
        '&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max',
    };
  }
  return {
    kind: 'lastYear',
    url: `https://archive-api.open-meteo.com/v1/archive?${common}&start_date=${shiftYear(startIso, -1)}&end_date=${shiftYear(endIso, -1)}` +
      '&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum',
  };
}

export function parseWeather(kind: WeatherKind, body: OpenMeteoDaily): WeatherDay[] {
  const d = body.daily;
  if (!d) return [];
  return d.time
    .map((date, i) => ({
      date,
      icon: weatherIcon(d.weather_code[i]),
      maxC: d.temperature_2m_max[i],
      minC: d.temperature_2m_min[i],
      precipitation: (kind === 'forecast' ? d.precipitation_probability_max?.[i] : d.precipitation_sum?.[i]) ?? null,
    }))
    .filter((x): x is WeatherDay => x.maxC !== null && x.minC !== null)
    .map((x) => ({ ...x, maxC: Math.round(x.maxC), minC: Math.round(x.minC) }));
}

@Injectable({ providedIn: 'root' })
export class WeatherService {
  private readonly http = inject(HttpClient);

  outlook(lat: number, lon: number, startIso: string, endIso: string, todayIso: string): Observable<WeatherOutlook> {
    const { kind, url } = weatherRequest(lat, lon, startIso, endIso, todayIso);
    return this.http.get<OpenMeteoDaily>(url).pipe(map((body) => ({ kind, days: parseWeather(kind, body) })));
  }
}
