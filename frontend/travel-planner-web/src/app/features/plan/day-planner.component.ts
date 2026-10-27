import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiService } from '../../core/api.service';
import { ApiError, toApiError } from '../../core/errors';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { ActivityPlaceDto, DayItemDto, DayPlanDto, INTERESTS, Interest, TripPace } from '../../core/models';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { ExternalLinkComponent } from '../../shared/external-link.component';
import { IconComponent, IconName } from '../../shared/icon.component';
import { DistancePipe, LocalDatePipe, MoneyPipe } from '../../shared/pipes';

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

interface Preferences {
  interests: Interest[];
  pace: TripPace;
}

/** The visitor's last interests and pace (per browser); storage may be unavailable. */
function loadPreferences(): Preferences {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    const value = raw ? (JSON.parse(raw) as Partial<Preferences>) : {};
    const interests = (value.interests ?? []).filter((i): i is Interest => INTERESTS.includes(i as Interest));
    const pace = value.pace === 'Relaxed' || value.pace === 'Intense' ? value.pace : 'Normal';
    return { interests: interests.length ? interests : DEFAULT_INTERESTS, pace };
  } catch {
    return { interests: DEFAULT_INTERESTS, pace: 'Normal' };
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
  imports: [TranslatePipe, IconComponent, ExternalLinkComponent, LocalDatePipe, MoneyPipe, DistancePipe, ErrorBannerComponent],
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
              <ol class="day-timeline">
                @for (item of day.items; track $index) {
                  @let key = day.number + '-' + $index;
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
                            <app-external-link [href]="place.directionsUrl" [label]="('days.howToGet' | t) + ' · ' + (place.distanceMeters | distance)" />
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
              </ol>
            </li>
          }
        </ol>

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
                      <span class="slot-label">{{ e.genre ?? e.segment }}</span>
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
  protected readonly plan = signal<DayPlanDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  /** Which suggestion is shown per slot (0 = the planner's pick, then the alternatives). */
  private readonly choices = signal<Record<string, number>>({});

  protected readonly request = computed(() => ({ id: this.planId(), interests: this.interests(), pace: this.pace(), lang: this.i18n.lang() }));

  constructor() {
    effect((onCleanup) => {
      const r = this.request();
      this.loading.set(true);
      this.error.set(null);
      const sub = this.api
        .getDayPlan(r.id, r.interests, r.pace)
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
    savePreferences({ interests: this.interests(), pace: this.pace() });
  }

  protected setPace(p: TripPace): void {
    this.pace.set(p);
    savePreferences({ interests: this.interests(), pace: p });
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
