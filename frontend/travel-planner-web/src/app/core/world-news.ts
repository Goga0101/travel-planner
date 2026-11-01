import { Injectable, signal } from '@angular/core';
import { Lang } from './i18n/i18n.service';

/** public/news/world-news.json, written by scripts/news/build-news.mjs from the world-news-editor agent's draft. */
export type NewsCategory = 'safety' | 'transport' | 'events' | 'weather' | 'rules' | 'tourism';
export type NewsImportance = 'alert' | 'notice' | 'info';
export type NewsRegion = 'europe' | 'asia' | 'middleEastAfrica' | 'americas' | 'oceania';
export type Localized = Record<Lang, string>;

export interface NewsPlace {
  code: string;
  city: Localized;
  iata: string | null;
}

export interface NewsItem {
  id: string;
  country: string;
  category: NewsCategory;
  importance: NewsImportance;
  publishedAt: string;
  expiresAt: string;
  sourceId: string;
  url: string;
  originalLanguage: string;
  title: Localized;
  summary: Localized;
  tip?: Localized;
  places: NewsPlace[];
}

export interface NewsFile {
  generatedAt: string;
  countries: Record<string, { name: Localized; region: NewsRegion }>;
  sources: Record<string, { name: string; url: string; language: string; kind: string }>;
  items: NewsItem[];
  /** Catalog city / airport → country, so a page that only knows "barcelona" or "BCN" finds the country's news. */
  lookup?: {
    cities: Record<string, string>;
    airports: Record<string, string>;
    countryNames: Record<string, Localized>;
    cityNames?: Record<string, Localized>;
    cityAirports?: Record<string, string | null>;
  };
}

/** The country for whatever the page knows: a country code, a catalog city code or an airport code. */
export function resolveCountry(file: NewsFile, ref: { country?: string | null; city?: string | null; airport?: string | null }): string | null {
  if (ref.country && /^[A-Z]{2}$/.test(ref.country)) return ref.country;
  if (ref.city && file.lookup?.cities[ref.city]) return file.lookup.cities[ref.city];
  if (ref.airport && file.lookup?.airports[ref.airport.toUpperCase()]) return file.lookup.airports[ref.airport.toUpperCase()];
  return null;
}

/**
 * The stories for one trip: that country's live stories, most important first. Stories about the trip's city come
 * before other news of the same importance.
 */
export function tripNews(file: NewsFile, country: string, city: string | null = null, now = Date.now()): NewsItem[] {
  const own = liveItems(file, now).filter((i) => i.country === country);
  const aboutCity = (i: NewsItem) => (city && i.places.some((p) => p.code === city) ? 0 : 1);
  return sortNews(own, 'important').sort((a, b) =>
    IMPORTANCE_RANK[a.importance] - IMPORTANCE_RANK[b.importance] || aboutCity(a) - aboutCity(b));
}

export const NEWS_CATEGORIES: readonly NewsCategory[] = ['safety', 'transport', 'events', 'weather', 'rules', 'tourism'];
export const NEWS_REGIONS: readonly NewsRegion[] = ['europe', 'asia', 'middleEastAfrica', 'americas', 'oceania'];
export type NewsSort = 'important' | 'newest';

export interface NewsFilter {
  category: NewsCategory | null;
  region: NewsRegion | null;
  country: string | null;
  query: string;
}

const IMPORTANCE_RANK: Record<NewsImportance, number> = { alert: 0, notice: 1, info: 2 };

/** Stories still worth showing at `now`: the build drops expired ones, but the file can be days old. */
export function liveItems(file: NewsFile, now = Date.now()): NewsItem[] {
  return file.items.filter((i) => Date.parse(i.expiresAt) > now);
}

/** Applies the page filters. The text search looks at both languages, so a Latin city name also finds Georgian stories. */
export function filterNews(items: readonly NewsItem[], file: NewsFile, f: NewsFilter): NewsItem[] {
  const q = f.query.trim().toLowerCase();
  return items.filter((i) => {
    if (f.category && i.category !== f.category) return false;
    if (f.country && i.country !== f.country) return false;
    if (f.region && file.countries[i.country]?.region !== f.region) return false;
    if (!q) return true;
    const country = file.countries[i.country]?.name;
    const haystack = [i.title.ka, i.title.en, i.summary.ka, i.summary.en, country?.ka, country?.en, ...i.places.flatMap((p) => [p.city.ka, p.city.en])]
      .join(' ')
      .toLowerCase();
    return haystack.includes(q);
  });
}

/**
 * "important": alerts first, then notices, then the rest; newest first within each.
 * "newest": by publication time only.
 */
export function sortNews(items: readonly NewsItem[], sort: NewsSort): NewsItem[] {
  return [...items].sort((a, b) =>
    sort === 'important'
      ? IMPORTANCE_RANK[a.importance] - IMPORTANCE_RANK[b.importance] || b.publishedAt.localeCompare(a.publishedAt)
      : b.publishedAt.localeCompare(a.publishedAt),
  );
}

export type DayGroup = 'today' | 'yesterday' | 'week' | 'older';

/** Groups for the "newest" view, by the reader's local calendar day. */
export function dayGroup(iso: string, now = new Date()): DayGroup {
  const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  const t = Date.parse(iso);
  if (t >= startOfToday) return 'today';
  if (t >= startOfToday - 86_400_000) return 'yesterday';
  if (t >= startOfToday - 6 * 86_400_000) return 'week';
  return 'older';
}

/** Counts per key, for the filter chips. */
export function countBy<T extends string>(items: readonly NewsItem[], key: (i: NewsItem) => T | undefined): Map<T, number> {
  const counts = new Map<T, number>();
  for (const i of items) {
    const k = key(i);
    if (k !== undefined) counts.set(k, (counts.get(k) ?? 0) + 1);
  }
  return counts;
}

/** Loads the news file once, next to index.html so it works under a sub-path such as /travel-planner/. */
@Injectable({ providedIn: 'root' })
export class WorldNewsService {
  readonly file = signal<NewsFile | null>(null);
  readonly state = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');

  load(): void {
    if (this.state() === 'loading' || this.state() === 'ready') return;
    this.state.set('loading');
    fetch('news/world-news.json', { cache: 'no-cache' })
      .then((r) => (r.ok ? (r.json() as Promise<NewsFile>) : Promise.reject(new Error(`HTTP ${r.status}`))))
      .then((json) => {
        this.file.set({ ...json, items: json.items ?? [], countries: json.countries ?? {}, sources: json.sources ?? {} });
        this.state.set('ready');
      })
      .catch(() => this.state.set('error'));
  }
}
