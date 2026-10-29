import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, Subscription, catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ClientConfigService } from '../../core/client-config.service';
import { ApiError, toApiError } from '../../core/errors';
import { toIsoDate } from '../../core/format';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { AirportDto, FlightCalendarDto, FlightCalendarRouteDto, FlightOptionsDto, PlaceDto } from '../../core/models';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe, MoneyPipe } from '../../shared/pipes';
import { DayPick, FareMonthsComponent } from './fare-months.component';
import { FlightOptionsComponent } from './flight-options.component';

/** A picked end of the route: the code sent to the API and the name shown. */
interface End {
  code: string;
  label: string;
}

type TripType = 'round' | 'oneway';

const ORIGIN_KEY = 'tp.origin';
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

/** The departure airport the visitor picked last on the search page (per browser); storage may be unavailable. */
function rememberedOrigin(): End | null {
  try {
    const raw = localStorage.getItem(ORIGIN_KEY);
    const value = raw ? (JSON.parse(raw) as AirportDto) : null;
    return value && /^[A-Z]{3}$/.test(value.iataCode) ? { code: value.iataCode, label: `${value.city} (${value.iataCode})` } : null;
  } catch {
    return null;
  }
}

/**
 * When can I fly from A to B? No dates needed: month calendars of the cheapest known fare per day for the route and
 * for nearby airports. Picking a departure day (and, for a round trip, a return day from the return calendar) lists
 * the flights for those dates with their prices and booking links. Every choice is kept in the URL, so it can be shared.
 */
@Component({
  selector: 'app-flight-calendar-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, ErrorBannerComponent, LocalDatePipe, MoneyPipe, FareMonthsComponent, FlightOptionsComponent],
  styles: `
    .fc-form { display: grid; gap: 1rem; }
    .fc-row { display: grid; gap: 0.75rem; align-items: end; }
    .fc-route-row { grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr); }
    .fc-when-row { grid-template-columns: auto minmax(0, 1fr) minmax(0, 1fr) 7rem 7rem auto; }
    @media (max-width: 900px) {
      .fc-when-row { grid-template-columns: 1fr 1fr; }
      .fc-when-row .segmented, .fc-when-row .btn { grid-column: 1 / -1; }
    }
    @media (max-width: 560px) {
      .fc-route-row { grid-template-columns: 1fr; }
      .fc-route-row .swap-btn { justify-self: center; transform: rotate(90deg); }
    }
    .fc-field { position: relative; display: grid; gap: 0.3rem; min-width: 0; }
    .fc-field input, .fc-field select { width: 100%; }
    .fc-field input:disabled { opacity: 0.5; }
    .fc-combo-list { position: absolute; top: 100%; left: 0; right: 0; z-index: 20; margin: 4px 0 0; padding: 0.3rem; list-style: none;
      background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); box-shadow: var(--shadow-lg); max-height: 19rem; overflow: auto; }
    .fc-combo-list li { padding: 0.5rem 0.6rem; border-radius: var(--radius-xs); cursor: pointer; display: flex; gap: 0.5rem; align-items: baseline; }
    .fc-combo-list li:hover { background: var(--surface-tint); }
    .fc-step { margin-top: 1.5rem; }
    .fc-step-head { display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem; }
    .fc-step-num { flex: none; width: 1.9rem; height: 1.9rem; border-radius: 999px; display: grid; place-items: center; font-weight: 700;
      background: var(--primary-deep); color: #fff; font-size: 0.9rem; }
    .fc-step-head h2 { font-size: 1.2rem; margin: 0; }
    .fc-step-head p { margin: 0.1rem 0 0; }
    .fc-routes { display: grid; gap: 1rem; }
    .fc-route { transition: border-color 0.15s, box-shadow 0.15s; }
    .fc-route.chosen { border-color: var(--primary); box-shadow: 0 0 0 3px var(--primary-ring); }
    .fc-route-head { display: flex; flex-wrap: wrap; gap: 0.4rem 0.9rem; align-items: center; margin-bottom: 0.85rem; }
    .fc-route-head h3 { font-size: 1.05rem; margin: 0; }
    .fc-chip { font-size: 0.78rem; padding: 0.15rem 0.6rem; border-radius: 999px; background: var(--surface-tint); color: var(--text-2); border: 1px solid var(--border); }
    .fc-chip.primary { background: var(--primary-soft); color: var(--primary-deep); border-color: transparent; font-weight: 600; }
    .fc-route-meta { margin-left: auto; font-size: 0.85rem; color: var(--muted); }
    .fc-summary { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; margin-bottom: 1rem; }
    .fc-summary .fc-chip { font-size: 0.85rem; padding: 0.3rem 0.75rem; }
    .fc-skeleton { height: 14rem; border-radius: var(--radius-lg); background: linear-gradient(90deg, var(--surface-tint), var(--surface), var(--surface-tint));
      background-size: 200% 100%; animation: fc-shimmer 1.2s linear infinite; }
    @keyframes fc-shimmer { to { background-position: -200% 0; } }
    .fc-note { margin-top: 1.25rem; }
  `,
  template: `
    <div class="container page">
      <header class="transit-head">
        <p class="eyebrow"><app-icon name="calendar" [size]="14" /> {{ 'fcal.eyebrow' | t }}</p>
        <h1 class="page-title">{{ 'fcal.title' | t }}</h1>
        <p class="muted">{{ 'fcal.lead' | t }}</p>
      </header>

      <form class="card fc-form" (submit)="$event.preventDefault(); search()">
        <div class="fc-row fc-route-row">
          <div class="fc-field">
            <label for="fc-from">{{ 'fcal.from' | t }}</label>
            <input id="fc-from" type="text" autocomplete="off" [placeholder]="'search.originPlaceholder' | t" [value]="fromText()"
                   (input)="onFromInput($any($event.target).value)" (focus)="$any($event.target).select()" (blur)="fromOpen.set(false)" />
            @if (fromOpen() && fromOptions().length) {
              <ul class="fc-combo-list" role="listbox">
                @for (a of fromOptions(); track a.iataCode) {
                  <li role="option" aria-selected="false" (mousedown)="$event.preventDefault(); pickFrom(a)">
                    <span class="iata">{{ a.iataCode }}</span><span><strong>{{ a.city }}</strong> <span class="muted small">{{ a.name }} · {{ a.country ?? a.countryCode }}</span></span>
                  </li>
                }
              </ul>
            }
          </div>
          <button type="button" class="swap-btn" (click)="swap()" [title]="'transit.swap' | t" [attr.aria-label]="'transit.swap' | t">
            <app-icon name="shuffle" [size]="18" />
          </button>
          <div class="fc-field">
            <label for="fc-to">{{ 'fcal.to' | t }}</label>
            <input id="fc-to" type="text" autocomplete="off" [placeholder]="'fcal.toPlaceholder' | t" [value]="toText()"
                   (input)="onToInput($any($event.target).value)" (focus)="$any($event.target).select()" (blur)="toOpen.set(false)" />
            @if (toOpen() && toOptions().length) {
              <ul class="fc-combo-list" role="listbox">
                @for (p of toOptions(); track p.cityCode) {
                  <li role="option" aria-selected="false" (mousedown)="$event.preventDefault(); pickTo(p)">
                    <span class="iata">{{ p.gatewayKm != null ? p.airportCodes[0] : p.cityCode }}</span>
                    <span><strong>{{ p.city }}</strong> <span class="muted small">{{ p.country }} · {{ p.airportCodes.join(', ') }}</span></span>
                  </li>
                }
              </ul>
            }
          </div>
        </div>

        <div class="fc-row fc-when-row">
          <div class="segmented" role="radiogroup" [attr.aria-label]="'fcal.tripType' | t">
            <button type="button" role="radio" [class.active]="trip() === 'round'" [attr.aria-checked]="trip() === 'round'" (click)="setTrip('round')">
              {{ 'fcal.roundTrip' | t }}
            </button>
            <button type="button" role="radio" [class.active]="trip() === 'oneway'" [attr.aria-checked]="trip() === 'oneway'" (click)="setTrip('oneway')">
              {{ 'fcal.oneWay' | t }}
            </button>
          </div>
          <div class="fc-field">
            <label for="fc-dep"><app-icon name="calendar" [size]="14" /> {{ 'fcal.departure' | t }}</label>
            <input id="fc-dep" type="date" [min]="today" [value]="departure() ?? ''" [attr.lang]="i18n.lang()" (change)="setDeparture($any($event.target).value)" />
          </div>
          <div class="fc-field">
            <label for="fc-ret"><app-icon name="calendar" [size]="14" /> {{ 'fcal.return' | t }}</label>
            <input id="fc-ret" type="date" [min]="departure() ?? today" [value]="returnDate() ?? ''" [disabled]="trip() === 'oneway'"
                   [attr.lang]="i18n.lang()" (change)="setReturn($any($event.target).value)" />
          </div>
          <div class="fc-field">
            <label for="fc-adults"><app-icon name="users" [size]="14" /> {{ 'search.travelers' | t }}</label>
            <input id="fc-adults" type="number" min="1" max="9" [value]="adults()" (change)="setAdults($any($event.target).value)" />
          </div>
          <div class="fc-field">
            <label for="fc-currency">{{ 'search.currency' | t }}</label>
            <select id="fc-currency" (change)="setCurrency($any($event.target).value)">
              @for (c of currencies(); track c) { <option [value]="c" [selected]="c === currency()">{{ c }}</option> }
            </select>
          </div>
          <button type="submit" class="btn btn-deep" [disabled]="!from() || !to() || loading()">
            @if (loading()) { <span class="spinner" aria-hidden="true"></span> } @else { <app-icon name="search" [size]="16" /> }
            {{ 'fcal.submit' | t }}
          </button>
        </div>
      </form>

      <app-error-banner [error]="error()" />

      @if (loading()) {
        <section class="fc-step" aria-busy="true">
          <p class="muted" aria-live="polite"><span class="spinner" aria-hidden="true"></span> {{ 'fcal.loading' | t }}</p>
          <div class="fc-skeleton"></div>
        </section>
      } @else if (calendar(); as cal) {
        @if (!cal.available) {
          <p class="card muted fc-step">{{ 'fcal.unavailable' | t }}</p>
        } @else {
          <!-- 1. Departure day, for the route and nearby airports -->
          <section class="fc-step" aria-labelledby="fc-step1">
            <div class="fc-step-head">
              <span class="fc-step-num">1</span>
              <div>
                <h2 id="fc-step1">{{ 'fcal.step1' | t }}</h2>
                <p class="small muted">{{ 'fcal.step1Hint' | t }}</p>
              </div>
            </div>
            <div class="fc-routes">
              @for (route of cal.routes; track route.fromCode + route.toCode) {
                <article class="card fc-route" [class.chosen]="isChosen(route)" [attr.aria-label]="routeName(route)">
                  <div class="fc-route-head">
                    <h3>{{ routeName(route) }}</h3>
                    @if (route.fromKm != null) { <span class="fc-chip">{{ 'fcal.nearbyFrom' | t: { km: route.fromKm, city: requestedFrom() } }}</span> }
                    @if (route.toKm != null) { <span class="fc-chip">{{ 'fcal.nearbyTo' | t: { km: route.toKm, city: requestedTo() } }}</span> }
                    @if (isChosen(route) && departure()) { <span class="fc-chip primary"><app-icon name="check" [size]="12" /> {{ 'fcal.chosen' | t }}</span> }
                    @if (route.days.length) {
                      <span class="fc-route-meta">{{ 'fcal.summary' | t: { count: route.days.length } }} ·
                        {{ 'fcal.priceFrom' | t: { price: (cheapest(route) | money: cal.currency) } }}</span>
                    }
                  </div>
                  @if (!route.days.length) {
                    <p class="muted small">
                      {{ 'fcal.none' | t: { route: routeName(route) } }}
                      @if (cal.routes.length > 1) { {{ 'fcal.noneNearby' | t }} }
                    </p>
                  }
                  @if (route.days.length || cal.routes.length === 1) {
                    <app-fare-months [days]="route.days" [currency]="cal.currency"
                                     [selected]="isChosen(route) ? departure() : null"
                                     [rangeStart]="isChosen(route) && trip() === 'round' ? departure() : null"
                                     [rangeEnd]="isChosen(route) && trip() === 'round' ? returnDate() : null"
                                     (picked)="pickDeparture(route, $event)" />
                  }
                </article>
              }
            </div>
          </section>

          <!-- 2. Return day, from the return calendar of the chosen route -->
          @if (trip() === 'round' && departure() && chosen(); as route) {
            <section class="fc-step" id="fc-return" aria-labelledby="fc-step2">
              <div class="fc-step-head">
                <span class="fc-step-num">2</span>
                <div>
                  <h2 id="fc-step2">{{ 'fcal.step2' | t }}</h2>
                  <p class="small muted">{{ 'fcal.step2Hint' | t: { route: returnName(route) } }}</p>
                </div>
              </div>
              <article class="card">
                @if (returnLoading()) {
                  <div class="fc-skeleton"></div>
                } @else {
                  <div class="fc-route-head">
                    <h3>{{ returnName(route) }}</h3>
                    @if (returnDays().length) {
                      <span class="fc-route-meta">{{ 'fcal.summary' | t: { count: returnDays().length } }} ·
                        {{ 'fcal.priceFrom' | t: { price: (cheapestOf(returnDays()) | money: cal.currency) } }}</span>
                    } @else {
                      <span class="fc-route-meta">{{ 'fcal.noReturnFares' | t }}</span>
                    }
                  </div>
                  <app-fare-months [days]="returnDays()" [currency]="cal.currency" [minDate]="departure()"
                                   [selected]="returnDate()" [rangeStart]="departure()" [rangeEnd]="returnDate()"
                                   (picked)="setReturn($event.date)" />
                }
              </article>
            </section>
          }
        }
      }

      <!-- 3. The flights for the dates -->
      @if (datesReady()) {
        <section class="fc-step" id="fc-options" aria-labelledby="fc-step3">
          <div class="fc-step-head">
            <span class="fc-step-num">{{ trip() === 'round' ? 3 : 2 }}</span>
            <div>
              <h2 id="fc-step3">{{ 'fcal.step3' | t }}</h2>
              <p class="small muted">{{ 'fcal.step3Hint' | t }}</p>
            </div>
          </div>
          <div class="fc-summary">
            <span class="fc-chip primary"><app-icon name="plane" [size]="13" /> {{ optionsRoute() }}</span>
            <span class="fc-chip"><app-icon name="calendar" [size]="13" /> {{ departure() | ldate: 'EEE, d MMM' }}
              @if (trip() === 'round' && returnDate()) { – {{ returnDate() | ldate: 'EEE, d MMM' }} · {{ 'unit.days' | t: { count: tripDays() } }} }</span>
            <span class="fc-chip"><app-icon name="users" [size]="13" /> {{ 'fcal.travelers' | t: { count: adults() } }}</span>
          </div>
          @if (optionsLoading()) {
            <p class="muted" aria-live="polite"><span class="spinner" aria-hidden="true"></span> {{ 'fcal.optionsLoading' | t }}</p>
            <div class="fc-skeleton"></div>
          } @else if (options(); as o) {
            <app-flight-options [result]="o" />
          }
          <app-error-banner [error]="optionsError()" />
        </section>
      }

      @if (calendar()?.available) {
        <p class="small muted fc-note">{{ 'fcal.note' | t }}</p>
      }
    </div>
  `,
})
export class FlightCalendarPage {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private readonly config = inject(ClientConfigService);
  protected readonly i18n = inject(I18nService);
  protected readonly today = toIsoDate(new Date());

  protected readonly from = signal<End | null>(rememberedOrigin());
  protected readonly to = signal<End | null>(null);
  protected readonly fromText = signal(this.from()?.label ?? '');
  protected readonly toText = signal('');
  protected readonly fromOptions = signal<AirportDto[]>([]);
  protected readonly toOptions = signal<PlaceDto[]>([]);
  protected readonly fromOpen = signal(false);
  protected readonly toOpen = signal(false);
  protected readonly currency = signal('EUR');
  protected readonly trip = signal<TripType>('round');
  protected readonly adults = signal(1);
  protected readonly departure = signal<string | null>(null);
  protected readonly returnDate = signal<string | null>(null);

  protected readonly loading = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly calendar = signal<FlightCalendarDto | null>(null);
  /** The names asked for, so a nearby route can say what it is near to. */
  protected readonly requestedFrom = signal('');
  protected readonly requestedTo = signal('');
  /** Codes of the route the dates belong to: the one asked, or a nearby one the visitor picked a day on. */
  private readonly chosenCodes = signal<{ from: string; to: string } | null>(null);

  protected readonly returnDays = signal<FlightCalendarDto['routes'][number]['days']>([]);
  protected readonly returnLoading = signal(false);
  protected readonly options = signal<FlightOptionsDto | null>(null);
  protected readonly optionsLoading = signal(false);
  protected readonly optionsError = signal<ApiError | null>(null);

  protected readonly currencies = computed(() => this.config.config()?.supportedCurrencies ?? ['EUR']);
  protected readonly chosen = computed(() => {
    const routes = this.calendar()?.routes ?? [];
    const codes = this.chosenCodes();
    return (codes && routes.find((r) => r.fromCode === codes.from && r.toCode === codes.to))
      ?? routes.find((r) => r.isRequested && r.days.length) ?? routes.find((r) => r.days.length) ?? routes[0] ?? null;
  });
  protected readonly datesReady = computed(() => !!this.departure() && (this.trip() === 'oneway' || !!this.returnDate()) && !!this.endsForOptions());
  protected readonly tripDays = computed(() => {
    const a = this.departure();
    const b = this.returnDate();
    return a && b ? Math.round((Date.parse(b) - Date.parse(a)) / 86_400_000) + 1 : 0;
  });
  protected readonly optionsRoute = computed(() => {
    const route = this.chosen();
    return route ? this.routeName(route) : `${this.from()?.label} → ${this.to()?.label}`;
  });

  private readonly fromTerms = new Subject<string>();
  private readonly toTerms = new Subject<string>();
  private calendarRequest?: Subscription;
  private returnRequest?: Subscription;
  private optionsRequest?: Subscription;

  constructor() {
    this.fromTerms
      .pipe(debounceTime(200), distinctUntilChanged(),
        switchMap((t) => (t.trim().length < 2 ? of([]) : this.api.searchAirports(t.trim()).pipe(catchError(() => of([]))))),
        takeUntilDestroyed(this.destroyRef))
      .subscribe((list) => this.fromOptions.set(list.slice(0, 8)));
    this.toTerms
      .pipe(debounceTime(250), distinctUntilChanged(),
        switchMap((t) => (t.trim().length < 2 ? of([]) : this.api.searchPlaces(t.trim()).pipe(catchError(() => of([]))))),
        takeUntilDestroyed(this.destroyRef))
      .subscribe((list) => this.toOptions.set(list.slice(0, 8)));
    this.destroyRef.onDestroy(() => {
      this.calendarRequest?.unsubscribe();
      this.returnRequest?.unsubscribe();
      this.optionsRequest?.unsubscribe();
    });

    // Shareable links: ?from=SAN&fromName=…&to=BUE&toName=…&trip=round&dep=…&ret=…&adults=1&currency=EUR&rf=LAX&rt=BUE
    const q = this.route.snapshot.queryParamMap;
    const code = (key: string) => (/^[A-Za-z]{3}$/.test(q.get(key) ?? '') ? q.get(key)!.toUpperCase() : null);
    const date = (key: string) => (ISO_DATE.test(q.get(key) ?? '') && q.get(key)! >= this.today ? q.get(key) : null);
    if (code('from')) this.setFrom({ code: code('from')!, label: q.get('fromName') ?? code('from')! });
    if (code('to')) this.setTo({ code: code('to')!, label: q.get('toName') ?? code('to')! });
    if (q.get('currency')) this.currency.set(q.get('currency')!.toUpperCase());
    if (q.get('trip') === 'oneway') this.trip.set('oneway');
    const adults = Number(q.get('adults'));
    if (adults >= 1 && adults <= 9) this.adults.set(Math.floor(adults));
    this.departure.set(date('dep'));
    this.returnDate.set(this.departure() && date('ret') && date('ret')! >= this.departure()! ? date('ret') : null);
    if (code('rf') && code('rt')) this.chosenCodes.set({ from: code('rf')!, to: code('rt')! });
    if (this.from() && this.to()) this.search(false);
  }

  // ---- route ----

  protected onFromInput(value: string): void {
    this.fromText.set(value);
    this.fromOpen.set(true);
    this.fromTerms.next(value);
  }

  protected onToInput(value: string): void {
    this.toText.set(value);
    this.toOpen.set(true);
    this.toTerms.next(value);
  }

  protected pickFrom(a: AirportDto): void {
    this.setFrom({ code: a.iataCode, label: `${a.city} (${a.iataCode})` });
    if (this.to()) this.search();
  }

  /** A town without an airport is flown to through its nearest airport. */
  protected pickTo(p: PlaceDto): void {
    const code = p.gatewayKm != null ? p.airportCodes[0] : p.cityCode;
    this.setTo({ code, label: p.gatewayKm != null ? `${p.city} (${code})` : p.city });
    if (this.from()) this.search();
  }

  protected swap(): void {
    const from = this.from();
    const to = this.to();
    this.from.set(to);
    this.to.set(from);
    this.fromText.set(to?.label ?? '');
    this.toText.set(from?.label ?? '');
    if (this.from() && this.to() && this.calendar()) this.search();
  }

  /** Loads the calendars; keeps the dates (a shared link, or new airports for the same trip) unless they are reset. */
  protected search(resetRoute = true): void {
    const from = this.from();
    const to = this.to();
    if (!from || !to) return;
    if (resetRoute) this.chosenCodes.set(null);
    this.returnDays.set([]);
    this.calendarRequest?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.requestedFrom.set(from.label);
    this.requestedTo.set(to.label);
    this.calendarRequest = this.api.getFlightCalendar(from.code, to.code, this.currency()).subscribe({
      next: (r) => {
        this.calendar.set(r);
        this.loading.set(false);
        this.loadReturnCalendar();
        this.loadOptions();
      },
      error: (e) => {
        this.error.set(toApiError(e));
        this.calendar.set(null);
        this.loading.set(false);
      },
    });
    this.syncUrl();
  }

  // ---- dates ----

  protected setTrip(trip: TripType): void {
    if (trip === this.trip()) return;
    this.trip.set(trip);
    this.loadReturnCalendar();
    this.loadOptions();
  }

  protected pickDeparture(route: FlightCalendarRouteDto, pick: DayPick): void {
    const routeChanged = this.chosen() !== route;
    this.chosenCodes.set({ from: route.fromCode, to: route.toCode });
    this.departure.set(pick.date);
    if (this.returnDate() && this.returnDate()! < pick.date) this.returnDate.set(null);
    if (routeChanged) this.returnDays.set([]);
    this.loadReturnCalendar(routeChanged);
    this.loadOptions();
    this.scrollTo(this.trip() === 'round' && !this.returnDate() ? 'fc-return' : 'fc-options');
  }

  protected setDeparture(value: string): void {
    this.departure.set(ISO_DATE.test(value) ? value : null);
    if (this.returnDate() && this.departure() && this.returnDate()! < this.departure()!) this.returnDate.set(null);
    this.loadReturnCalendar();
    this.loadOptions();
  }

  protected setReturn(value: string): void {
    this.returnDate.set(ISO_DATE.test(value) && (!this.departure() || value >= this.departure()!) ? value : null);
    this.loadOptions();
    if (this.returnDate()) this.scrollTo('fc-options');
  }

  protected setAdults(value: string): void {
    const n = Math.min(9, Math.max(1, Math.floor(Number(value) || 1)));
    this.adults.set(n);
    this.loadOptions();
  }

  protected setCurrency(value: string): void {
    this.currency.set(value);
    if (this.calendar()) this.search(false);
  }

  // ---- loading ----

  /** The return calendar of the chosen route (destination → origin), without nearby airports. */
  private loadReturnCalendar(force = false): void {
    const route = this.chosen();
    if (this.trip() !== 'round' || !this.departure() || !route || !this.calendar()?.available) return;
    if (!force && this.returnDays().length && !this.returnLoading()) return;
    this.returnRequest?.unsubscribe();
    this.returnLoading.set(true);
    this.returnRequest = this.api.getFlightCalendar(route.toCode, route.fromCode, this.currency(), false).subscribe({
      next: (r) => {
        this.returnDays.set(r.routes[0]?.days ?? []);
        this.returnLoading.set(false);
      },
      error: () => {
        this.returnDays.set([]);
        this.returnLoading.set(false);
      },
    });
  }

  private loadOptions(): void {
    this.syncUrl();
    this.optionsRequest?.unsubscribe();
    this.options.set(null);
    this.optionsError.set(null);
    const ends = this.endsForOptions();
    const dep = this.departure();
    const ret = this.trip() === 'round' ? this.returnDate() : null;
    if (!ends || !dep || (this.trip() === 'round' && !ret)) {
      this.optionsLoading.set(false);
      return;
    }
    this.optionsLoading.set(true);
    this.optionsRequest = this.api.getFlightOptions(ends.from, ends.to, dep, ret, this.adults(), this.currency()).subscribe({
      next: (r) => {
        this.options.set(r);
        this.optionsLoading.set(false);
      },
      error: (e) => {
        this.optionsError.set(toApiError(e));
        this.optionsLoading.set(false);
      },
    });
  }

  /** The chosen calendar route, or the airports typed when the calendar is not loaded yet. */
  private endsForOptions(): { from: string; to: string } | null {
    const route = this.chosen();
    if (route) return { from: route.fromCode, to: route.toCode };
    const from = this.from();
    const to = this.to();
    return from && to ? { from: from.code, to: to.code } : null;
  }

  // ---- display ----

  protected isChosen(route: FlightCalendarRouteDto): boolean {
    return this.chosen() === route;
  }

  /** The route asked for keeps the names the visitor picked; nearby ones use the city names from the API. */
  protected routeName(route: FlightCalendarRouteDto): string {
    const from = route.fromKm == null ? this.requestedFrom() : `${route.fromCity} (${route.fromCode})`;
    const to = route.toKm == null ? this.requestedTo() : `${route.toCity} (${route.toCode})`;
    return `${from} → ${to}`;
  }

  protected returnName(route: FlightCalendarRouteDto): string {
    return this.routeName(route).split(' → ').reverse().join(' → ');
  }

  protected cheapest(route: FlightCalendarRouteDto): number {
    return this.cheapestOf(route.days);
  }

  protected cheapestOf(days: { price: number }[]): number {
    return Math.min(...days.map((d) => d.price));
  }

  private setFrom(end: End): void {
    this.from.set(end);
    this.fromText.set(end.label);
    this.fromOpen.set(false);
    this.fromOptions.set([]);
  }

  private setTo(end: End): void {
    this.to.set(end);
    this.toText.set(end.label);
    this.toOpen.set(false);
    this.toOptions.set([]);
  }

  private syncUrl(): void {
    const from = this.from();
    const to = this.to();
    if (!from || !to) return;
    const chosen = this.chosenCodes();
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        from: from.code, fromName: from.label, to: to.code, toName: to.label, currency: this.currency(), trip: this.trip(),
        dep: this.departure(), ret: this.trip() === 'round' ? this.returnDate() : null, adults: this.adults() > 1 ? this.adults() : null,
        rf: chosen?.from ?? null, rt: chosen?.to ?? null,
      },
      replaceUrl: true,
    });
  }

  private scrollTo(id: string): void {
    setTimeout(() => this.document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 60);
  }
}
