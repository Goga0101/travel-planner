import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, Subscription, catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ClientConfigService } from '../../core/client-config.service';
import { ApiError, toApiError } from '../../core/errors';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { AirportDto, FlightCalendarDayDto, FlightCalendarDto, FlightCalendarRouteDto, PlaceDto } from '../../core/models';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { ExternalLinkComponent } from '../../shared/external-link.component';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe, MoneyPipe, StopsPipe } from '../../shared/pipes';

/** A picked end of the route: the code sent to the API and the name shown. */
interface End {
  code: string;
  label: string;
}

interface MonthView {
  key: string;
  first: Date;
  /** Leading blanks (Monday-first weeks), then each day of the month with its fare, if any. */
  blanks: number[];
  days: { day: number; iso: string; fare: FlightCalendarDayDto | null }[];
  cheapest: number;
}

const ORIGIN_KEY = 'tp.origin';

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
 * When can I fly from A to B? No dates needed: the cheapest known one-way fare of each day over the coming twelve
 * months, shown as month calendars, for the route asked and for nearby airports that have fares.
 */
@Component({
  selector: 'app-flight-calendar-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, ErrorBannerComponent, ExternalLinkComponent, LocalDatePipe, MoneyPipe, StopsPipe],
  styles: `
    .fc-form { display: grid; gap: 0.75rem; grid-template-columns: 1fr auto 1fr auto auto; align-items: end; }
    @media (max-width: 760px) { .fc-form { grid-template-columns: 1fr; } .fc-form .swap-btn { justify-self: center; } }
    .fc-field { position: relative; display: grid; gap: 0.3rem; }
    .fc-field input, .fc-field select { width: 100%; }
    .fc-routes { display: grid; gap: 1.25rem; margin-top: 1.25rem; }
    .fc-route-head { display: flex; flex-wrap: wrap; gap: 0.5rem 1rem; align-items: baseline; justify-content: space-between; margin-bottom: 0.75rem; }
    .fc-route-head h2 { font-size: 1.15rem; margin: 0; }
    .fc-badge { display: inline-block; font-size: 0.8rem; padding: 0.1rem 0.55rem; border-radius: 999px; background: var(--surface-2, #eef3f6); color: var(--text-muted, #556); }
    .fc-months { display: grid; gap: 1rem; grid-template-columns: repeat(auto-fill, minmax(min(100%, 290px), 1fr)); }
    .fc-month { min-width: 0; }
    .fc-month h3 { font-size: 0.95rem; margin: 0 0 0.4rem; text-transform: capitalize; }
    .fc-grid { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 3px; }
    .fc-wd { font-size: 0.7rem; text-align: center; color: var(--text-muted, #667); }
    .fc-day { min-height: 2.6rem; border-radius: 6px; padding: 2px; text-align: center; font-size: 0.75rem; color: var(--text-muted, #889);
      border: 1px solid transparent; background: none; min-width: 0; overflow: hidden; display: flex; flex-direction: column; align-items: center; justify-content: center; }
    button.fc-day { cursor: pointer; color: inherit; background: var(--surface-2, #f1f5f8); border-color: var(--border, #dde4ea); }
    button.fc-day:hover { border-color: var(--border-strong, #9ab); }
    button.fc-day.cheap { background: #e3f4ea; border-color: #9fd3b4; }
    button.fc-day.selected { outline: 2px solid var(--navy, #12395b); outline-offset: 1px; }
    .fc-price { font-weight: 600; font-size: 0.68rem; white-space: nowrap; letter-spacing: -0.02em; }
    .fc-detail { margin-top: 0.9rem; padding: 0.75rem 0.9rem; border-radius: 10px; background: var(--surface-2, #f1f5f8);
      display: flex; flex-wrap: wrap; gap: 0.4rem 1.25rem; align-items: center; }
    .fc-combo-list { position: absolute; top: 100%; left: 0; right: 0; z-index: 20; margin: 2px 0 0; padding: 0.25rem; list-style: none;
      background: var(--surface, #fff); border: 1px solid var(--border, #dde4ea); border-radius: 8px; box-shadow: 0 8px 24px rgba(0,0,0,.12); max-height: 18rem; overflow: auto; }
    .fc-combo-list li { padding: 0.4rem 0.55rem; border-radius: 6px; cursor: pointer; }
    .fc-combo-list li:hover { background: var(--surface-2, #f1f5f8); }
    .dark-muted { color: var(--text-muted, #667); }
  `,
  template: `
    <div class="container page">
      <header class="transit-head">
        <p class="eyebrow"><app-icon name="calendar" [size]="14" /> {{ 'fcal.eyebrow' | t }}</p>
        <h1 class="page-title">{{ 'fcal.title' | t }}</h1>
        <p class="muted">{{ 'fcal.lead' | t }}</p>
      </header>

      <form class="card fc-form" (submit)="$event.preventDefault(); search()">
        <div class="fc-field">
          <label for="fc-from">{{ 'fcal.from' | t }}</label>
          <input id="fc-from" type="text" autocomplete="off" [placeholder]="'search.originPlaceholder' | t" [value]="fromText()"
                 (input)="onFromInput($any($event.target).value)" (focus)="$any($event.target).select()" (blur)="fromOpen.set(false)" />
          @if (fromOpen() && fromOptions().length) {
            <ul class="fc-combo-list" role="listbox">
              @for (a of fromOptions(); track a.iataCode) {
                <li role="option" aria-selected="false" (mousedown)="$event.preventDefault(); pickFrom(a)">
                  <strong>{{ a.city }}</strong> <span class="iata">{{ a.iataCode }}</span> <span class="muted small">{{ a.name }} · {{ a.country ?? a.countryCode }}</span>
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
                  <strong>{{ p.city }}</strong> <span class="muted small">{{ p.country }} · {{ p.airportCodes.join(', ') }}</span>
                </li>
              }
            </ul>
          }
        </div>
        <div class="fc-field">
          <label for="fc-currency">{{ 'search.currency' | t }}</label>
          <select id="fc-currency" [value]="currency()" (change)="currency.set($any($event.target).value)">
            @for (c of currencies(); track c) { <option [value]="c" [selected]="c === currency()">{{ c }}</option> }
          </select>
        </div>
        <button type="submit" class="btn btn-deep" [disabled]="!from() || !to() || loading()">
          @if (loading()) { <span class="spinner" aria-hidden="true"></span> } @else { <app-icon name="search" [size]="16" /> }
          {{ 'fcal.submit' | t }}
        </button>
      </form>

      <app-error-banner [error]="error()" />

      @if (loading()) {
        <p class="muted" aria-live="polite"><span class="spinner" aria-hidden="true"></span> {{ 'fcal.loading' | t }}</p>
      } @else if (result(); as r) {
        @if (!r.available) {
          <p class="card muted">{{ 'fcal.unavailable' | t }}</p>
        } @else {
          <div class="fc-routes">
            @for (route of r.routes; track route.fromCode + route.toCode) {
              <section class="card" [attr.aria-label]="routeName(route)">
                <div class="fc-route-head">
                  <h2>{{ routeName(route) }}</h2>
                  @if (route.fromKm != null) { <span class="fc-badge">{{ 'fcal.nearbyFrom' | t: { km: route.fromKm, city: requestedFrom() } }}</span> }
                  @if (route.toKm != null) { <span class="fc-badge">{{ 'fcal.nearbyTo' | t: { km: route.toKm, city: requestedTo() } }}</span> }
                  @if (route.days.length) {
                    <span class="small muted">{{ 'fcal.summary' | t: { count: route.days.length } }} ·
                      {{ 'fcal.priceFrom' | t: { price: (cheapest(route) | money: r.currency) } }}</span>
                  }
                </div>
                @if (!route.days.length) {
                  <p class="muted">
                    {{ 'fcal.none' | t: { route: routeName(route) } }}
                    @if (r.routes.length > 1) { {{ 'fcal.noneNearby' | t }} }
                  </p>
                } @else {
                  <div class="fc-months">
                    @for (m of months(route); track m.key) {
                      <div class="fc-month">
                        <h3>{{ m.first | ldate: 'LLLL y' }}</h3>
                        <div class="fc-grid">
                          @for (w of weekdays(); track $index) { <span class="fc-wd">{{ w }}</span> }
                          @for (b of m.blanks; track $index) { <span></span> }
                          @for (d of m.days; track d.iso) {
                            @if (d.fare; as f) {
                              <button type="button" class="fc-day" [class.cheap]="f.price <= m.cheapest * 1.15"
                                      [class.selected]="selected()?.route === route && selected()?.fare === f"
                                      (click)="select(route, f)" [attr.aria-label]="(d.iso | ldate: 'd MMMM') + ' · ' + (f.price | money: r.currency)">
                                <span>{{ d.day }}</span>
                                <span class="fc-price">{{ shortPrice(f.price, r.currency) }}</span>
                              </button>
                            } @else {
                              <span class="fc-day">{{ d.day }}</span>
                            }
                          }
                        </div>
                      </div>
                    }
                  </div>
                  @if (selected(); as s) {
                    @if (s.route === route) {
                      <div class="fc-detail" aria-live="polite">
                        <strong>{{ s.fare.date | ldate: 'EEEE, d MMMM y' }}, {{ s.fare.time }}</strong>
                        <span>{{ s.fare.from }} → {{ s.fare.to }}</span>
                        <span>{{ s.fare.stops | stops }}</span>
                        @if (s.fare.airline) { <span class="muted">{{ s.fare.airline }}</span> }
                        <strong>{{ s.fare.price | money: r.currency }}</strong> <span class="small muted">{{ 'fcal.perPerson' | t }}</span>
                        <app-external-link [href]="s.fare.bookingUrl" [label]="'fcal.book' | t" [button]="true" />
                      </div>
                    }
                  }
                }
              </section>
            }
          </div>
          <p class="small muted" style="margin-top: 1rem">{{ 'fcal.note' | t }}</p>
        }
      }
    </div>
  `,
})
export class FlightCalendarPage {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly config = inject(ClientConfigService);
  protected readonly i18n = inject(I18nService);

  protected readonly from = signal<End | null>(rememberedOrigin());
  protected readonly to = signal<End | null>(null);
  protected readonly fromText = signal(this.from()?.label ?? '');
  protected readonly toText = signal('');
  protected readonly fromOptions = signal<AirportDto[]>([]);
  protected readonly toOptions = signal<PlaceDto[]>([]);
  protected readonly fromOpen = signal(false);
  protected readonly toOpen = signal(false);
  protected readonly currency = signal('EUR');
  protected readonly loading = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly result = signal<FlightCalendarDto | null>(null);
  protected readonly selected = signal<{ route: FlightCalendarRouteDto; fare: FlightCalendarDayDto } | null>(null);
  protected readonly currencies = computed(() => this.config.config()?.supportedCurrencies ?? ['EUR']);
  /** The names asked for, so a nearby route can say what it is near to. */
  protected readonly requestedFrom = signal('');
  protected readonly requestedTo = signal('');

  /** Monday-first short weekday names in the page language. */
  protected readonly weekdays = computed(() => {
    const format = new Intl.DateTimeFormat(this.i18n.intlLocale(), { weekday: 'narrow' });
    return [0, 1, 2, 3, 4, 5, 6].map((i) => format.format(new Date(2024, 0, 1 + i))); // 1 Jan 2024 was a Monday
  });

  private readonly fromTerms = new Subject<string>();
  private readonly toTerms = new Subject<string>();
  private request?: Subscription;
  private readonly monthCache = new WeakMap<FlightCalendarRouteDto, MonthView[]>();

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

    // Shareable links: ?from=SAN&fromName=…&to=BUE&toName=…&currency=EUR searches straight away.
    const q = this.route.snapshot.queryParamMap;
    const from = q.get('from');
    const to = q.get('to');
    if (from && /^[A-Za-z]{3}$/.test(from)) this.setFrom({ code: from.toUpperCase(), label: q.get('fromName') ?? from.toUpperCase() });
    if (to && /^[A-Za-z]{3}$/.test(to)) this.setTo({ code: to.toUpperCase(), label: q.get('toName') ?? to.toUpperCase() });
    const currency = q.get('currency');
    if (currency) this.currency.set(currency.toUpperCase());
    if (this.from() && this.to()) this.search();
  }

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
  }

  /** A town without an airport is flown to through its nearest airport. */
  protected pickTo(p: PlaceDto): void {
    const code = p.gatewayKm != null ? p.airportCodes[0] : p.cityCode;
    this.setTo({ code, label: p.gatewayKm != null ? `${p.city} (${code})` : p.city });
  }

  protected swap(): void {
    const from = this.from();
    const to = this.to();
    this.from.set(to);
    this.to.set(from);
    this.fromText.set(to?.label ?? '');
    this.toText.set(from?.label ?? '');
    if (this.from() && this.to() && this.result()) this.search();
  }

  protected search(): void {
    const from = this.from();
    const to = this.to();
    if (!from || !to) return;
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.selected.set(null);
    this.requestedFrom.set(from.label);
    this.requestedTo.set(to.label);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { from: from.code, fromName: from.label, to: to.code, toName: to.label, currency: this.currency() },
      replaceUrl: true,
    });
    this.request = this.api.getFlightCalendar(from.code, to.code, this.currency()).subscribe({
      next: (r) => {
        this.result.set(r);
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(toApiError(e));
        this.result.set(null);
        this.loading.set(false);
      },
    });
  }

  protected select(route: FlightCalendarRouteDto, fare: FlightCalendarDayDto): void {
    this.selected.set({ route, fare });
  }

  /** The route asked for keeps the names the visitor picked; nearby ones use the city names from the API. */
  protected routeName(route: FlightCalendarRouteDto): string {
    const from = route.fromKm == null ? this.requestedFrom() : `${route.fromCity} (${route.fromCode})`;
    const to = route.toKm == null ? this.requestedTo() : `${route.toCity} (${route.toCode})`;
    return `${from} → ${to}`;
  }

  /** Whole units for the calendar cells ("€384"), where cents would not fit. */
  protected shortPrice(price: number, currency: string): string {
    return new Intl.NumberFormat(this.i18n.intlLocale(), { style: 'currency', currency, maximumFractionDigits: 0 }).format(price);
  }

  protected cheapest(route: FlightCalendarRouteDto): number {
    return Math.min(...route.days.map((d) => d.price));
  }

  /** Months from the first to the last month with a fare, each laid out as a Monday-first calendar. */
  protected months(route: FlightCalendarRouteDto): MonthView[] {
    const cached = this.monthCache.get(route);
    if (cached) return cached;
    const byDate = new Map(route.days.map((d) => [d.date, d]));
    const keys = [...new Set(route.days.map((d) => d.date.slice(0, 7)))].sort();
    const views = keys.map((key) => {
      const [year, month] = key.split('-').map(Number);
      const first = new Date(year, month - 1, 1);
      const length = new Date(year, month, 0).getDate();
      const days = Array.from({ length }, (_, i) => {
        const iso = `${key}-${String(i + 1).padStart(2, '0')}`;
        return { day: i + 1, iso, fare: byDate.get(iso) ?? null };
      });
      const prices = days.filter((d) => d.fare).map((d) => d.fare!.price);
      return { key, first, blanks: Array((first.getDay() + 6) % 7).fill(0), days, cheapest: Math.min(...prices) };
    });
    this.monthCache.set(route, views);
    return views;
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
}
