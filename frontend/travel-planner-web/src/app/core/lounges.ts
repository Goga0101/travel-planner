import { Injectable, computed, signal } from '@angular/core';

/** Lounge networks a card can open. */
export type LoungeProgram = 'PriorityPass' | 'LoungeKey' | 'DragonPass' | 'MastercardTravelPass' | 'VisaAirportCompanion' | 'BankOwn' | 'Other';
export type PlaceKind = 'lounge' | 'dining' | 'spa' | 'sleep';

export interface Localized { ka: string; en: string }

export interface ProgramInfo {
  id: LoungeProgram;
  name: string;
  summary: Localized;
  url: string;
}

export interface CardProgram {
  program: LoungeProgram;
  /** Free visits a year; null with unlimited = no limit, null without it = not stated by the bank. */
  visitsPerYear: number | null;
  unlimited: boolean;
  guest: string | null;
  dining: boolean | null;
}

export interface LoungeCard {
  id: string;
  /** ISO country of the issuing bank ("GE"). */
  country: string;
  bank: Localized;
  card: string;
  network: string;
  programs: CardProgram[];
  conditions: Localized | null;
  source: string;
}

export interface LoungePlace {
  name: string;
  kind: PlaceKind;
  terminal: string | null;
  airside: boolean | null;
  schengen: 'schengen' | 'non-schengen' | 'both' | null;
  location: string | null;
  hours: string | null;
  programs: LoungeProgram[];
  offer: string | null;
  /** A bank-owned lounge (program BankOwn): the bank whose cards open it (English name). */
  bank?: string;
  source: string;
}

export interface LoungeAirport {
  iata: string;
  airport: string;
  city: string;
  country: string;
  places: LoungePlace[];
  notes: string | null;
}

export interface LoungeData {
  checked: string;
  programs: ProgramInfo[];
  cards: LoungeCard[];
  airports: LoungeAirport[];
}

/** A place at an airport and the picked cards that open it. */
export interface PlaceMatch {
  place: LoungePlace;
  cards: LoungeCard[];
}

/**
 * Programs a place takes. Mastercard Travel Pass and Visa Airport Companion (how Georgian Visa premium cards enter lounges
 * since October 2024) both run on DragonPass's network, so a DragonPass place takes them too.
 */
export function programsOpening(place: LoungePlace): Set<LoungeProgram> {
  const set = new Set(place.programs);
  if (set.has('DragonPass')) { set.add('MastercardTravelPass'); set.add('VisaAirportCompanion'); }
  return set;
}

/** The cards (of those given) that open the place. */
export function cardsFor(place: LoungePlace, cards: readonly LoungeCard[]): LoungeCard[] {
  // A bank's own lounge space opens to that bank's cards.
  if (place.bank) return cards.filter((c) => c.bank.en === place.bank);
  const open = programsOpening(place);
  return cards.filter((c) => c.programs.some((p) => open.has(p.program)));
}

/** Places at the airport, with the given cards that open each; places the cards open come first. */
export function matchPlaces(airport: LoungeAirport | null | undefined, cards: readonly LoungeCard[]): PlaceMatch[] {
  if (!airport) return [];
  return airport.places
    .map((place) => ({ place, cards: cardsFor(place, cards) }))
    .sort((a, b) => Number(b.cards.length > 0) - Number(a.cards.length > 0));
}

const CARDS_KEY = 'tp.loungeCards';
const COUNTRY_KEY = 'tp.loungeCountry';

function readList(key: string): string[] {
  try {
    const v = JSON.parse(localStorage.getItem(key) ?? '[]');
    return Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : [];
  } catch {
    return [];
  }
}

/**
 * Lounge data (public/lounges/lounges.json, researched from the banks and the lounge networks) and the cards the visitor
 * says they hold, kept in this browser. Loaded once, on first use.
 */
@Injectable({ providedIn: 'root' })
export class LoungeService {
  private readonly state = signal<LoungeData | null>(null);
  private loading?: Promise<void>;

  readonly data = this.state.asReadonly();
  /** Issuing country of the cards to choose from (Georgia first, the only one for now). */
  readonly country = signal<string>(readCountry());
  readonly myCardIds = signal<string[]>(readList(CARDS_KEY));
  /** The airport the lounge dialog shows (null = closed). */
  readonly dialogAirport = signal<string | null>(null);

  readonly countries = computed(() => [...new Set((this.state()?.cards ?? []).map((c) => c.country))]);
  readonly cardsOfCountry = computed(() => (this.state()?.cards ?? []).filter((c) => c.country === this.country()));
  readonly myCards = computed(() => {
    const ids = new Set(this.myCardIds());
    return (this.state()?.cards ?? []).filter((c) => ids.has(c.id));
  });
  private readonly byIata = computed(() => new Map((this.state()?.airports ?? []).map((a) => [a.iata, a])));

  load(): Promise<void> {
    this.loading ??= fetch('lounges/lounges.json', { cache: 'no-cache' })
      .then((r) => (r.ok ? r.json() : null))
      .then((d: LoungeData | null) => this.state.set(d))
      .catch(() => this.state.set(null));
    return this.loading;
  }

  airport(iata: string | null | undefined): LoungeAirport | null {
    return iata ? this.byIata().get(iata.toUpperCase()) ?? null : null;
  }

  program(id: LoungeProgram): ProgramInfo | null {
    return this.state()?.programs.find((p) => p.id === id) ?? null;
  }

  toggleCard(id: string): void {
    const ids = this.myCardIds();
    this.setCards(ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]);
  }

  setCards(ids: string[]): void {
    this.myCardIds.set(ids);
    try { localStorage.setItem(CARDS_KEY, JSON.stringify(ids)); } catch { /* storage unavailable */ }
  }

  setCountry(code: string): void {
    this.country.set(code);
    try { localStorage.setItem(COUNTRY_KEY, code); } catch { /* storage unavailable */ }
  }

  openDialog(iata: string): void {
    void this.load();
    this.dialogAirport.set(iata.toUpperCase());
  }
}

function readCountry(): string {
  try { return localStorage.getItem(COUNTRY_KEY) || 'GE'; } catch { return 'GE'; }
}
