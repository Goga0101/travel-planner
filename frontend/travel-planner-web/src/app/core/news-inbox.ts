import { Injectable, computed, inject, signal } from '@angular/core';
import { NewsFile, NewsItem, WorldNewsService, liveItems, resolveCountry, tripNews } from './world-news';

/** A trip the visitor looked at: its country, and its city when the page knew it. */
export interface NewsVisit {
  country: string;
  city: string | null;
  visitedAt: string;
}

/** The stories of one viewed city (or country), newest-important first. */
export interface InboxGroup {
  key: string;
  country: string;
  city: string | null;
  stories: NewsItem[];
}

export interface Inbox {
  /** Alerts are pinned above the groups and not repeated inside them. */
  alerts: NewsItem[];
  groups: InboxGroup[];
}

/** A card dropping in, with what its postage stamp shows. */
export interface DroppedStory {
  item: NewsItem;
  city: string | null;
  stamp: string;
}

const VISITS_KEY = 'tp.newsVisits';
const READ_KEY = 'tp.newsRead';
const SEEN_KEY = 'tp.tripNewsSeen';
const MAX_VISITS = 8;
const MAX_DROPS = 3;

/**
 * The visitor's news inbox: the latest stories of every city they looked at, grouped by city, with the alerts on top.
 * A story goes to the group of the city it is about, else to the most recently viewed city of its country.
 */
export function buildInbox(file: NewsFile, visits: readonly NewsVisit[], now = Date.now()): Inbox {
  const groups: InboxGroup[] = [];
  const keys = new Set<string>();
  for (const v of [...visits].sort((a, b) => b.visitedAt.localeCompare(a.visitedAt))) {
    const key = v.city ?? v.country;
    if (keys.has(key)) continue;
    keys.add(key);
    groups.push({ key, country: v.country, city: v.city, stories: [] });
  }

  const alerts: NewsItem[] = [];
  const placed = new Set<string>();
  const live = liveItems(file, now);
  for (const country of new Set(groups.map((g) => g.country))) {
    const ofCountry = groups.filter((g) => g.country === country);
    for (const story of tripNews({ ...file, items: live }, country, null, now)) {
      if (placed.has(story.id)) continue;
      placed.add(story.id);
      if (story.importance === 'alert') { alerts.push(story); continue; }
      const own = ofCountry.find((g) => g.city && story.places.some((p) => p.code === g.city));
      (own ?? ofCountry[0]).stories.push(story);
    }
  }
  alerts.sort((a, b) => b.publishedAt.localeCompare(a.publishedAt));
  return { alerts, groups: groups.filter((g) => g.stories.length) };
}

function readJson<T>(key: string, storage: () => Storage, fallback: T): T {
  try {
    const raw = storage().getItem(key);
    return raw ? (JSON.parse(raw) as T) : fallback;
  } catch {
    return fallback;
  }
}

function writeJson(key: string, storage: () => Storage, value: unknown): void {
  try { storage().setItem(key, JSON.stringify(value)); } catch { /* storage unavailable: kept in memory */ }
}

/**
 * Remembers, in this browser only, which trips the visitor looked at and which stories they read; drives the cards
 * that drop in and the inbox in the header. Nothing is sent to the server.
 */
@Injectable({ providedIn: 'root' })
export class NewsInboxService {
  private readonly news = inject(WorldNewsService);

  readonly visits = signal<NewsVisit[]>(readJson<NewsVisit[]>(VISITS_KEY, () => localStorage, []));
  readonly read = signal<ReadonlySet<string>>(new Set(readJson<string[]>(READ_KEY, () => localStorage, [])));
  /** Cards waiting to drop in (consumed by the drop host in the app shell). */
  readonly drops = signal<DroppedStory[]>([]);
  /** Bumped each time a card lands in the inbox, so the icon can react. */
  readonly landings = signal(0);
  readonly panelOpen = signal(false);
  /** The story to show expanded when the panel opens from a card's "Read more". */
  readonly focusStory = signal<string | null>(null);
  /** Where cards fly to: the header inbox button. */
  inboxAnchor: HTMLElement | null = null;

  readonly inbox = computed<Inbox>(() => {
    const f = this.news.file();
    return f ? buildInbox(f, this.visits()) : { alerts: [], groups: [] };
  });
  private readonly allStories = computed(() => [...this.inbox().alerts, ...this.inbox().groups.flatMap((g) => g.stories)]);
  readonly unread = computed(() => this.allStories().filter((s) => !this.read().has(s.id)).length);
  readonly total = computed(() => this.allStories().length);

  constructor() {
    this.news.load();
  }

  /**
   * The visitor opened a trip to this place: add it to the inbox and, the first time in this browser session,
   * drop in its most important unread stories.
   */
  visit(ref: { country?: string | null; city?: string | null; airport?: string | null }): void {
    const file = this.news.file();
    if (!file) return;
    const country = resolveCountry(file, ref);
    if (!country) return;
    const city = ref.city ?? (ref.airport ? this.cityOfAirport(file, ref.airport) : null);

    const rest = this.visits().filter((v) => (v.city ?? v.country) !== (city ?? country));
    const visits = [{ country, city, visitedAt: new Date().toISOString() }, ...rest].slice(0, MAX_VISITS);
    this.visits.set(visits);
    writeJson(VISITS_KEY, () => localStorage, visits);

    if (this.seenThisSession(country)) return;
    this.markSeen(country);
    const stamp = (ref.airport ?? (city ? file.lookup?.cityAirports?.[city] : null) ?? country).toUpperCase();
    const unread = tripNews(file, country, city).filter((s) => !this.read().has(s.id)).slice(0, MAX_DROPS);
    if (unread.length) this.drops.set(unread.map((item) => ({ item, city, stamp: item.places[0]?.iata ?? stamp })));
  }

  markRead(id: string): void {
    if (this.read().has(id)) return;
    this.saveRead(new Set(this.read()).add(id));
  }

  markAllRead(): void {
    const next = new Set(this.read());
    for (const s of this.allStories()) next.add(s.id);
    this.saveRead(next);
  }

  forget(groupKey: string): void {
    const visits = this.visits().filter((v) => (v.city ?? v.country) !== groupKey);
    this.visits.set(visits);
    writeJson(VISITS_KEY, () => localStorage, visits);
  }

  open(storyId: string | null = null): void {
    this.focusStory.set(storyId);
    this.panelOpen.set(true);
  }

  landed(): void {
    this.landings.update((n) => n + 1);
  }

  cityName(code: string | null, lang: 'ka' | 'en'): string | null {
    return code ? this.news.file()?.lookup?.cityNames?.[code]?.[lang] ?? null : null;
  }

  countryName(code: string, lang: 'ka' | 'en'): string {
    const f = this.news.file();
    return (f?.countries[code]?.name ?? f?.lookup?.countryNames[code])?.[lang] ?? code;
  }

  sourceName(id: string): string {
    return this.news.file()?.sources[id]?.name ?? id;
  }

  private saveRead(set: Set<string>): void {
    // Only ids of stories still published are worth keeping.
    const live = new Set(this.news.file()?.items.map((i) => i.id) ?? []);
    const kept = [...set].filter((id) => live.size === 0 || live.has(id));
    this.read.set(new Set(kept));
    writeJson(READ_KEY, () => localStorage, kept);
  }

  private cityOfAirport(file: NewsFile, airport: string): string | null {
    const code = airport.toUpperCase();
    const airports = file.lookup?.cityAirports ?? {};
    return Object.keys(airports).find((city) => airports[city] === code) ?? null;
  }

  private seenThisSession(country: string): boolean {
    return readJson<string[]>(SEEN_KEY, () => sessionStorage, []).includes(country);
  }

  private markSeen(country: string): void {
    const list = readJson<string[]>(SEEN_KEY, () => sessionStorage, []);
    writeJson(SEEN_KEY, () => sessionStorage, [...new Set([...list, country])]);
  }
}
