import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiService } from '../../core/api.service';
import { ApiError, toApiError } from '../../core/errors';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ActivityPlaceDto, DayItemDto, DayPlanDto, INTERESTS, Interest, PlaceSuggestionDto, PlanDayDto, RouteDirectionDto, TransitItineraryDto, TripPace } from '../../core/models';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { SelectedStayService } from '../../core/selected-stay.service';
import { ExternalLinkComponent } from '../../shared/external-link.component';
import { IconComponent, IconName } from '../../shared/icon.component';
import { DistancePipe, LocalDatePipe, MoneyPipe } from '../../shared/pipes';
import { ItineraryComponent, routeTags } from './itinerary.component';
import { LiveMapComponent } from '../transit/live-map.component';

const STORAGE_KEY = 'tp.dayPlanner';
const DEFAULT_INTERESTS: Interest[] = ['culture', 'food'];

export const INTEREST_ICONS: Record<Interest, IconName> = {
  culture: 'landmark',
  museums: 'museum',
  food: 'utensils',
  cafes: 'coffee',
  bars: 'wine',
  nightlife: 'music',
  nature: 'tree',
  shopping: 'bag',
  shows: 'ticket',
};

export interface Stop {
  name: string;
  latitude: number;
  longitude: number;
}

/** Getting from one stop of the day to the next: walk when it is short, otherwise public transport (loaded on demand). */
export interface DayHop {
  key: string;
  from: Stop;
  to: Stop;
  meters: number;
  walkMinutes: number;
  walk: boolean;
  date: string;
  /** Local time to set off so the next stop starts on time. */
  leave: string;
  directionsUrl: string;
}

export interface DayChain {
  start: Stop;
  /** The hop that leads to each item (null when the item has no location). */
  hops: (DayHop | null)[];
  /** Back to the start at the end of the day (not on the departure day). */
  back: DayHop | null;
  mapUrl: string | null;
}

/** Hops up to this many minutes on foot are walked; longer ones suggest public transport. */
export const WALK_LIMIT_MINUTES = 15;

export function distanceMeters(a: Stop, b: Stop): number {
  const rad = Math.PI / 180;
  const dLat = (b.latitude - a.latitude) * rad;
  const dLon = (b.longitude - a.longitude) * rad;
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(a.latitude * rad) * Math.cos(b.latitude * rad) * Math.sin(dLon / 2) ** 2;
  return Math.round(2 * 6371000 * Math.asin(Math.sqrt(h)));
}

/** Streets are not straight lines (~1.3×), and people walk ~80 m a minute. */
export function walkingMinutes(meters: number): number {
  return Math.max(1, Math.round((meters * 1.3) / 80));
}

function toMinutes(hm: string): number {
  const [h, m] = hm.split(':').map(Number);
  return h * 60 + m;
}

function fromMinutes(total: number): string {
  const t = Math.min(Math.max(total, 0), 23 * 60 + 55);
  return `${String(Math.floor(t / 60)).padStart(2, '0')}:${String(t % 60).padStart(2, '0')}`;
}

const coord = (s: Stop) => `${s.latitude.toFixed(6)},${s.longitude.toFixed(6)}`;

function directions(from: Stop, to: Stop, mode: 'walking' | 'transit'): string {
  return `https://www.google.com/maps/dir/?api=1&origin=${coord(from)}&destination=${coord(to)}&travelmode=${mode}`;
}

function hop(from: Stop, to: Stop, date: string, leave: string): DayHop {
  const meters = distanceMeters(from, to);
  const walkMinutes = walkingMinutes(meters);
  const walk = walkMinutes <= WALK_LIMIT_MINUTES;
  return {
    key: `${coord(from)}>${coord(to)}@${date}T${leave}`,
    from, to, meters, walkMinutes, walk, date, leave,
    directionsUrl: directions(from, to, walk ? 'walking' : 'transit'),
  };
}

/** Set off early enough: the walk (or a rough ride estimate) plus a few minutes, rounded down to 5 minutes. */
export function leaveFor(arriveAt: string, meters: number): string {
  const walk = walkingMinutes(meters);
  const travel = walk <= WALK_LIMIT_MINUTES ? walk : Math.round(walk / 3) + 12;
  const t = toMinutes(arriveAt) - travel - 5;
  return fromMinutes(t - (((t % 5) + 5) % 5));
}

/**
 * The day as a chain: start → each stop in order → back to the start. Items without a location (some events) are
 * skipped by the chain, which carries on from the last known stop.
 */
export function buildChain(day: PlanDayDto, start: Stop, stopOf: (item: DayItemDto, index: number) => Stop | null): DayChain {
  const hops: (DayHop | null)[] = [];
  const stops: Stop[] = [];
  let previous = start;
  let lastTime: string | null = null;
  day.items.forEach((item, i) => {
    const stop = stopOf(item, i);
    if (!stop) {
      hops.push(null);
      return;
    }
    hops.push(hop(previous, stop, day.date, leaveFor(item.time, distanceMeters(previous, stop))));
    stops.push(stop);
    previous = stop;
    lastTime = item.time;
  });
  const back = stops.length && day.kind !== 'Departure' && lastTime
    ? hop(previous, start, day.date, fromMinutes(toMinutes(lastTime) + 90))
    : null;
  const end = back ? start : stops[stops.length - 1];
  const via = (back ? stops : stops.slice(0, -1)).slice(0, 9);
  const mapUrl = stops.length
    ? `https://www.google.com/maps/dir/?api=1&origin=${coord(start)}&destination=${coord(end)}` +
      (via.length ? `&waypoints=${encodeURIComponent(via.map(coord).join('|'))}` : '') + '&travelmode=walking'
    : null;
  return { start, hops, back, mapUrl };
}

interface HopState {
  loading: boolean;
  data?: RouteDirectionDto;
  failed?: boolean;
  /** The hop's ends as map places (kept, so the map is not redrawn on every check). */
  ends?: { from: PlaceSuggestionDto; to: PlaceSuggestionDto };
  /** The option drawn on the hop's map. */
  pick?: number;
}

interface Preferences {
  interests: Interest[];
  pace: TripPace;
  music: string[];
}

/** The visitor's last interests and pace (per browser); storage may be unavailable. */
function loadPreferences(): Preferences {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    const value = raw ? (JSON.parse(raw) as Partial<Preferences>) : {};
    const interests = (value.interests ?? []).filter((i): i is Interest => INTERESTS.includes(i as Interest));
    const pace = value.pace === 'Relaxed' || value.pace === 'Intense' ? value.pace : 'Normal';
    const music = Array.isArray(value.music) ? value.music.filter((m): m is string => typeof m === 'string').slice(0, 20) : [];
    return { interests: interests.length ? interests : DEFAULT_INTERESTS, pace, music };
  } catch {
    return { interests: DEFAULT_INTERESTS, pace: 'Normal', music: [] };
  }
}

function savePreferences(p: Preferences): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(p));
  } catch {
    /* storage unavailable */
  }
}

/**
 * Day-by-day activity planner: the visitor picks what they enjoy and a pace; the server proposes places per slot
 * (walkable chains, notable first) with alternatives, plus the city's ticketed events during the trip.
 */
@Component({
  selector: 'app-day-planner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, ExternalLinkComponent, LocalDatePipe, MoneyPipe, DistancePipe, ErrorBannerComponent, NgTemplateOutlet, ItineraryComponent, LiveMapComponent],
  template: `
    <section class="card day-planner" id="days" aria-labelledby="days-title">
      <div class="section-head">
        <h2 id="days-title"><app-icon name="calendar" /> {{ 'days.title' | t }}</h2>
        @if (plan(); as p) {
          @if (p.arrivalTime || p.departureTime) {
            <span class="small muted">
              @if (p.arrivalTime) { {{ 'days.arrives' | t: { time: p.arrivalTime } }} }
              @if (p.departureTime) { · {{ 'days.departs' | t: { time: p.departureTime } }} }
            </span>
          }
        }
      </div>
      <p class="muted small">{{ 'days.lead' | t }}</p>
      <p class="small chain-hint"><app-icon name="walk" [size]="15" /> {{ 'days.chainHint' | t }}</p>

      <div class="planner-controls">
        <div class="sp-section">
          <span class="label" id="interests-label">{{ 'days.interests' | t }}</span>
          <div class="chips" role="group" aria-labelledby="interests-label">
            @for (i of allInterests; track i) {
              <button type="button" class="chip-toggle" [class.active]="interests().includes(i)" [attr.aria-pressed]="interests().includes(i)"
                      (click)="toggle(i)">
                <app-icon [name]="icons[i]" [size]="15" /> {{ interestKey(i) | t }}
              </button>
            }
          </div>
        </div>
        <div class="sp-section">
          <span class="label" id="pace-label">{{ 'days.pace' | t }}</span>
          <div class="pill-group" role="radiogroup" aria-labelledby="pace-label">
            @for (p of paces; track p) {
              <button type="button" role="radio" class="pill-btn" [class.active]="pace() === p" [attr.aria-checked]="pace() === p" (click)="setPace(p)">
                {{ paceKey(p) | t }}
              </button>
            }
          </div>
        </div>
      </div>

      @if (nightOut()) {
        <div class="music-filter">
          <div class="sp-section">
            <div class="sp-section-head">
              <span class="label" id="music-label">{{ 'days.music' | t }}</span>
              <span class="hint">{{ 'days.musicHint' | t }}</span>
            </div>
            @if (plan()?.musicGenres?.length) {
              <div class="chips" role="group" aria-labelledby="music-label">
                <button type="button" class="chip-toggle" [class.active]="music().length === 0" [attr.aria-pressed]="music().length === 0" (click)="clearMusic()">
                  {{ 'common.all' | t }}
                </button>
                @for (g of plan()!.musicGenres; track g.name) {
                  <button type="button" class="chip-toggle" [class.active]="music().includes(g.name)" [attr.aria-pressed]="music().includes(g.name)"
                          (click)="toggleMusic(g.name)">
                    <app-icon name="music" [size]="14" /> {{ g.name }} <span class="chip-count">{{ g.count }}</span>
                  </button>
                }
              </div>
            } @else if (plan()) {
              <p class="small muted">{{ 'days.noMusic' | t }}</p>
            }
          </div>
          @if (plan()?.residentAdvisorUrl; as ra) {
            <div class="ra-box">
              <span class="ra-badge" aria-hidden="true">RA</span>
              <div>
                <strong>{{ 'days.raTitle' | t }}</strong>
                <p class="small muted">{{ 'days.raText' | t }}</p>
              </div>
              <app-external-link [href]="ra" [label]="'days.raOpen' | t" [button]="true" />
            </div>
          }
        </div>
      }

      <app-error-banner [error]="error()" />

      @if (loading() && !plan()) {
        <p class="muted"><span class="spinner" aria-hidden="true"></span> {{ 'days.loading' | t }}</p>
      }

      @if (plan(); as p) {
        <ol class="day-list" [class.is-loading]="loading()" [attr.aria-busy]="loading()">
          @for (day of p.days; track day.number) {
            <li class="day-card">
              <header class="day-head">
                <span class="day-num">{{ 'days.day' | t: { n: day.number } }}</span>
                <strong>{{ day.date | ldate: 'EEEE, d MMM' }}</strong>
                @if (day.kind !== 'Full') { <span class="meta-chip">{{ kindKey(day.kind) | t }}</span> }
              </header>
              @if (day.items.length === 0) {
                <p class="muted small day-empty">
                  @if (day.kind === 'Departure') { {{ 'days.departureEmpty' | t: { time: p.departureTime ?? '' } }} }
                  @else if (day.kind === 'Arrival') { {{ 'days.arrivalEmpty' | t }} }
                  @else { {{ 'days.nothing' | t }} }
                </p>
              }
              @let chain = chains()[day.number];
              <ol class="day-timeline">
                @if (chain?.mapUrl) {
                  <li class="day-anchor">
                    <span class="anchor-icon"><app-icon name="bed" [size]="16" /></span>
                    <span class="anchor-text"><strong>{{ 'days.startAt' | t: { place: chain!.start.name } }}</strong></span>
                    <app-external-link [href]="chain!.mapUrl!" [label]="'days.dayRoute' | t" />
                  </li>
                }
                @for (item of day.items; track $index) {
                  @let key = day.number + '-' + $index;
                  @if (chain?.hops?.[$index]; as h) {
                    <ng-container [ngTemplateOutlet]="hopRow" [ngTemplateOutletContext]="{ $implicit: h }" />
                  }
                  <li class="day-item" [class.is-event]="!!item.event">
                    <span class="day-time">{{ item.time }}</span>
                    @if (item.event; as e) {
                      <div class="day-media">
                        @if (e.imageUrl) { <img [src]="e.imageUrl" alt="" loading="lazy" decoding="async" /> }
                        @else { <span class="media-icon"><app-icon name="ticket" [size]="26" /></span> }
                      </div>
                      <div class="day-body">
                        <span class="slot-label">{{ 'days.slot.event' | t }} · {{ e.genre ?? e.segment }}</span>
                        <h3>{{ e.name }}</h3>
                        @if (e.venue) { <p class="small muted"><app-icon name="pin" [size]="14" /> {{ e.venue }}</p> }
                        @if (e.priceMin !== undefined && e.priceMin !== null) {
                          <p class="small"><strong>{{ e.priceMin | money: e.currency }}@if (e.priceMax && e.priceMax !== e.priceMin) { – {{ e.priceMax | money: e.currency }} }</strong></p>
                        }
                        <div class="day-links">
                          <app-external-link [href]="e.url" [label]="'days.tickets' | t" />
                          <app-external-link [href]="e.directionsUrl" [label]="'days.howToGet' | t" />
                        </div>
                      </div>
                    } @else {
                      @let place = shown(key, item);
                      @if (place) {
                        <div class="day-media">
                          @if (place.imageUrl) { <img [src]="place.imageUrl" alt="" loading="lazy" decoding="async" /> }
                          @else { <span class="media-icon" [attr.data-interest]="place.interest"><app-icon [name]="icons[place.interest]" [size]="26" /></span> }
                        </div>
                        <div class="day-body">
                          <span class="slot-label"><app-icon [name]="icons[place.interest]" [size]="13" /> {{ slotKey(item.slot) | t }}</span>
                          <h3>{{ place.name }}</h3>
                          @if (place.summary) { <p class="small day-summary">{{ place.summary }}</p> }
                          <p class="small muted day-facts">
                            @if (place.cuisine) { <span>{{ place.cuisine }}</span> }
                            @if (place.openingHours) { <span><app-icon name="clock" [size]="13" /> {{ place.openingHours }}</span> }
                            @if (place.address) { <span>{{ place.address }}</span> }
                          </p>
                          <div class="day-links">
                            <app-external-link [href]="place.mapUrl" [label]="'days.map' | t" />
                            @if (place.website) { <app-external-link [href]="place.website" [label]="'days.website' | t" /> }
                            @if (item.alternatives.length) {
                              <button type="button" class="text-link small" (click)="swap(key, item)">
                                <app-icon name="shuffle" [size]="14" /> {{ 'days.other' | t: { n: choice(key) + 1, total: item.alternatives.length + 1 } }}
                              </button>
                            }
                          </div>
                        </div>
                      }
                    }
                  </li>
                }
                @if (chain?.back; as h) {
                  <ng-container [ngTemplateOutlet]="hopRow" [ngTemplateOutletContext]="{ $implicit: h }" />
                  <li class="day-anchor is-end">
                    <span class="anchor-icon"><app-icon name="bed" [size]="16" /></span>
                    <span class="anchor-text"><strong>{{ 'days.backTo' | t: { place: chain!.start.name } }}</strong></span>
                  </li>
                }
              </ol>
            </li>
          }
        </ol>

        <ng-template #hopRow let-h>
          <li class="day-hop" [class.is-walk]="h.walk" [class.open]="isOpen(h.key)">
            <span class="hop-icon"><app-icon [name]="h.walk ? 'walk' : 'metro'" [size]="15" /></span>
            <div class="hop-body">
              <div class="hop-line">
                <strong>{{ h.walk ? ('days.hopWalk' | t: { minutes: h.walkMinutes }) : ('days.hopRide' | t) }}</strong>
                <span class="small muted">· {{ h.meters | distance }} · {{ 'days.hopLeave' | t: { time: h.leave } }}</span>
                @if (!h.walk && h.walkMinutes <= 35) { <span class="small muted">· {{ 'days.hopOrWalk' | t: { minutes: h.walkMinutes } }}</span> }
              </div>
              <div class="hop-actions">
                <!-- Walks too: the visitor may prefer a ride, and the ticket is useful either way. -->
                <button type="button" class="hop-toggle" [attr.aria-expanded]="isOpen(h.key)" (click)="toggleHop(h)">
                  <app-icon name="ticket" [size]="14" /> {{ (isOpen(h.key) ? 'days.hopHide' : 'days.hopShow') | t }}
                </button>
                <app-external-link [href]="h.directionsUrl" [label]="'days.hopDirections' | t" />
              </div>
              @if (isOpen(h.key)) {
                @let st = hopState(h.key);
                @if (st?.loading) {
                  <p class="small muted"><span class="spinner" aria-hidden="true"></span> {{ 'days.hopLoading' | t }}</p>
                } @else if (st?.data; as r) {
                  @if (r.itineraries.length) {
                    @if (st!.ends; as e) {
                      <app-live-map class="hop-map" [vehicles]="false" [from]="e.from" [to]="e.to" [route]="r.itineraries[st!.pick ?? 0]" />
                    }
                    <ol class="route-options hop-options">
                      @for (it of r.itineraries.slice(0, 3); track $index; let first = $first; let i = $index) {
                        <li app-itinerary class="route-option is-pickable" [class.best]="first" [class.is-picked]="(st!.pick ?? 0) === i"
                            [itinerary]="it" [best]="first && !it.estimated" [tags]="tagsOf(r.itineraries)[i]" (click)="pickHop(h.key, i)"></li>
                      }
                    </ol>
                  } @else {
                    <p class="small muted">{{ 'days.hopNone' | t }}</p>
                  }
                  @if (!r.itineraries.length && r.cityTicket; as tk) {
                    <p class="leg-ticket hop-city-ticket" [class.online]="tk.buyOnline === true" [class.offline]="tk.buyOnline === false">
                      <app-icon name="ticket" [size]="14" />
                      <span>
                        <strong>{{ 'route.cityTickets' | t }}: {{ tk.name ?? ('route.ticket' | t) }}</strong>
                        @if (tk.price !== undefined && tk.price !== null) { · {{ tk.price | money: tk.currency }} }
                        @if (tk.buyOnline === true) { · {{ 'route.buyOnlineYes' | t }} }
                        @if (tk.buyOnline === false) { · {{ 'route.buyOnlineNo' | t }} }
                        @if (tk.note) { <br /><span class="muted">{{ tk.note }}</span> }
                      </span>
                      @if (tk.buyUrl) { <a class="leg-buy" [href]="tk.buyUrl" target="_blank" rel="noopener noreferrer nofollow">{{ 'route.buy' | t }} <app-icon name="external" [size]="12" /></a> }
                    </p>
                  }
                  <div class="route-alt">
                    <app-external-link [href]="r.googleTransitUrl" [label]="'route.googleTransit' | t" />
                    <app-external-link [href]="r.uberUrl" [label]="'days.hopTaxi' | t" />
                  </div>
                } @else if (st?.failed) {
                  <p class="small muted">{{ 'days.hopNone' | t }}</p>
                }
              }
            </div>
          </li>
        </ng-template>

        <div class="events-block">
          <h3><app-icon name="ticket" [size]="18" /> {{ 'days.eventsTitle' | t }}</h3>
          @if (p.eventsConfigured) {
            @if (p.events.length) {
              <ul class="event-grid">
                @for (e of p.events; track e.id) {
                  <li class="event-card">
                    <div class="event-media">
                      @if (e.imageUrl) { <img [src]="e.imageUrl" alt="" loading="lazy" decoding="async" /> }
                      @else { <span class="media-icon"><app-icon name="music" [size]="28" /></span> }
                      <span class="event-date">{{ e.date | ldate: 'd MMM' }} @if (e.time) { · {{ e.time }} }</span>
                    </div>
                    <div class="event-body">
                      <span class="slot-label">{{ e.genre ?? e.segment }}@if (e.subGenre) { · {{ e.subGenre }} }</span>
                      <strong>{{ e.name }}</strong>
                      @if (e.venue) { <span class="small muted">{{ e.venue }}</span> }
                      <span class="small">
                        @if (e.priceMin !== undefined && e.priceMin !== null) {
                          <strong>{{ e.priceMin | money: e.currency }}@if (e.priceMax && e.priceMax !== e.priceMin) { – {{ e.priceMax | money: e.currency }} }</strong>
                        } @else { <span class="muted">{{ 'days.priceOnSite' | t }}</span> }
                      </span>
                      <app-external-link [href]="e.url" [label]="'days.tickets' | t" />
                    </div>
                  </li>
                }
              </ul>
              <p class="small muted">{{ 'days.eventsSource' | t: { provider: p.eventsProvider ?? '' } }}</p>
            } @else {
              <p class="muted small">{{ 'days.noEvents' | t }}</p>
            }
          } @else {
            <p class="muted small">{{ 'days.eventsOff' | t }}</p>
            <app-external-link [href]="p.eventsSearchUrl" [label]="'days.eventsSearch' | t" />
          }
        </div>
        <p class="small muted source-note">{{ 'days.sourceNote' | t }}</p>
      }
    </section>
  `,
})
export class DayPlannerComponent {
  readonly planId = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly i18n = inject(I18nService);

  protected readonly allInterests = INTERESTS;
  protected readonly icons = INTEREST_ICONS;
  protected readonly paces: TripPace[] = ['Relaxed', 'Normal', 'Intense'];

  private readonly initial = loadPreferences();
  protected readonly interests = signal<Interest[]>(this.initial.interests);
  protected readonly pace = signal<TripPace>(this.initial.pace);
  protected readonly music = signal<string[]>(this.initial.music);
  /** The music filter and Resident Advisor link belong to nights out. */
  protected readonly nightOut = computed(() => this.interests().some((i) => i === 'nightlife' || i === 'bars' || i === 'shows'));
  protected readonly plan = signal<DayPlanDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  /** Which suggestion is shown per slot (0 = the planner's pick, then the alternatives). */
  private readonly choices = signal<Record<string, number>>({});

  private readonly stays = inject(SelectedStayService);
  /** Days start from the chosen hotel when there is one. */
  protected readonly stay = computed(() => {
    this.stays.all();
    return this.stays.get(this.planId());
  });

  protected readonly request = computed(() => ({
    id: this.planId(), interests: this.interests(), pace: this.pace(), music: this.music(), lang: this.i18n.lang(), stay: this.stay(),
  }));

  /** Each day as a chain from the stay through the shown stops and back, following the visitor's swaps. */
  protected readonly chains = computed<Record<number, DayChain>>(() => {
    const p = this.plan();
    if (!p) return {};
    this.choices();
    const stay = this.stay();
    const start: Stop = stay
      ? { name: stay.name, latitude: stay.latitude, longitude: stay.longitude }
      : { name: this.i18n.t('days.cityCentre'), latitude: p.startLatitude, longitude: p.startLongitude };
    const out: Record<number, DayChain> = {};
    for (const day of p.days) {
      out[day.number] = buildChain(day, start, (item, i) => {
        if (item.event) {
          const e = item.event;
          return e.latitude != null && e.longitude != null ? { name: e.venue ?? e.name, latitude: e.latitude, longitude: e.longitude } : null;
        }
        const place = this.shown(day.number + '-' + i, item);
        return place ? { name: place.name, latitude: place.latitude, longitude: place.longitude } : null;
      });
    }
    return out;
  });

  private readonly hopStates = signal<Record<string, HopState>>({});
  private readonly openHops = signal<ReadonlySet<string>>(new Set());

  protected isOpen(key: string): boolean {
    return this.openHops().has(key);
  }

  protected hopState(key: string): HopState | undefined {
    return this.hopStates()[key];
  }

  /** Opens a hop's public-transport options, loading them the first time. */
  protected toggleHop(h: DayHop): void {
    const open = new Set(this.openHops());
    if (open.has(h.key)) {
      open.delete(h.key);
      this.openHops.set(open);
      return;
    }
    open.add(h.key);
    this.openHops.set(open);
    const known = this.hopStates()[h.key];
    if (known && !known.failed) return;
    this.setHop(h.key, { loading: true });
    this.api
      .getHop(this.planId(), h.from, h.to, h.date, h.leave)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => this.setHop(h.key, {
          loading: false, data, pick: 0,
          ends: {
            from: { name: h.from.name, latitude: h.from.latitude, longitude: h.from.longitude, kind: 'place' },
            to: { name: h.to.name, latitude: h.to.latitude, longitude: h.to.longitude, kind: 'place' },
          },
        }),
        error: () => this.setHop(h.key, { loading: false, failed: true }),
      });
  }

  protected tagsOf(list: TransitItineraryDto[]): ReturnType<typeof routeTags> {
    return routeTags(list.slice(0, 3));
  }

  protected pickHop(key: string, pick: number): void {
    const st = this.hopStates()[key];
    if (st && st.pick !== pick) this.setHop(key, { ...st, pick });
  }

  private setHop(key: string, state: HopState): void {
    this.hopStates.set({ ...this.hopStates(), [key]: state });
  }

  constructor() {
    effect((onCleanup) => {
      const r = this.request();
      this.loading.set(true);
      this.error.set(null);
      const sub = this.api
        .getDayPlan(r.id, r.interests, r.pace, r.music, r.stay)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (p) => {
            this.plan.set(p);
            this.choices.set({});
            this.loading.set(false);
          },
          error: (e) => {
            this.error.set(toApiError(e));
            this.loading.set(false);
          },
        });
      onCleanup(() => sub.unsubscribe());
    });
  }

  protected toggle(i: Interest): void {
    const current = this.interests();
    const next = current.includes(i) ? current.filter((x) => x !== i) : [...current, i];
    this.interests.set(next.length ? next : current);
    this.save();
  }

  protected setPace(p: TripPace): void {
    this.pace.set(p);
    this.save();
  }

  protected toggleMusic(genre: string): void {
    const current = this.music();
    this.music.set(current.includes(genre) ? current.filter((g) => g !== genre) : [...current, genre]);
    this.save();
  }

  protected clearMusic(): void {
    this.music.set([]);
    this.save();
  }

  private save(): void {
    savePreferences({ interests: this.interests(), pace: this.pace(), music: this.music() });
  }

  protected choice(key: string): number {
    return this.choices()[key] ?? 0;
  }

  protected shown(key: string, item: DayItemDto): ActivityPlaceDto | undefined {
    const i = this.choice(key);
    return i === 0 ? item.place : item.alternatives[i - 1];
  }

  protected swap(key: string, item: DayItemDto): void {
    this.choices.set({ ...this.choices(), [key]: (this.choice(key) + 1) % (item.alternatives.length + 1) });
  }

  protected interestKey(i: Interest): TranslationKey {
    return `interest.${i}`;
  }

  protected paceKey(p: TripPace): TranslationKey {
    return `pace.${p}`;
  }

  protected kindKey(k: string): TranslationKey {
    return `days.kind.${k}` as TranslationKey;
  }

  protected slotKey(s: string): TranslationKey {
    return `days.slot.${s}` as TranslationKey;
  }
}
