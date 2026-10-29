import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, Subscription, catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ClientConfigService } from '../../core/client-config.service';
import { ApiError, toApiError } from '../../core/errors';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { AirportDto, FlightCalendarDto, FlightCalendarRouteDto, PlaceDto } from '../../core/models';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { IconComponent } from '../../shared/icon.component';
import { MoneyPipe } from '../../shared/pipes';
import { DayPick, FareMonthsComponent } from './fare-months.component';
import { PickedFlight, TripDialogComponent } from './trip-dialog.component';

/** A picked end of the route: the code sent to the API and the name shown. */
interface End {
  code: string;
  label: string;
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
 * When can I fly from A to B? No dates needed: month calendars of the cheapest known fare per day, for the route asked
 * and for nearby airports. Hovering a day shows its flight; clicking it asks whether a ticket back is wanted and lists
 * the flights for the chosen dates with their prices and booking links.
 */
@Component({
  selector: 'app-flight-calendar-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, ErrorBannerComponent, MoneyPipe, FareMonthsComponent, TripDialogComponent],
  styles: `
    .fc-form { display: grid; gap: 0.75rem; grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr) 7rem auto; align-items: end; }
    @media (max-width: 760px) {
      .fc-form { grid-template-columns: 1fr; }
      .fc-form .swap-btn { justify-self: center; transform: rotate(90deg); }
    }
    .fc-field { position: relative; display: grid; gap: 0.3rem; min-width: 0; }
    .fc-field input, .fc-field select { width: 100%; }
    .fc-combo-list { position: absolute; top: 100%; left: 0; right: 0; z-index: 20; margin: 4px 0 0; padding: 0.3rem; list-style: none;
      background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); box-shadow: var(--shadow-lg); max-height: 19rem; overflow: auto; }
    .fc-combo-list li { padding: 0.5rem 0.6rem; border-radius: var(--radius-xs); cursor: pointer; display: flex; gap: 0.5rem; align-items: baseline; }
    .fc-combo-list li:hover { background: var(--surface-tint); }
    .fc-routes { display: grid; gap: 1.25rem; margin-top: 1.25rem; }
    .fc-route-head { display: flex; flex-wrap: wrap; gap: 0.4rem 0.9rem; align-items: center; margin-bottom: 0.85rem; }
    .fc-route-head h2 { font-size: 1.15rem; margin: 0; }
    .fc-chip { font-size: 0.78rem; padding: 0.15rem 0.6rem; border-radius: 999px; background: var(--surface-tint); color: var(--text-2); border: 1px solid var(--border); }
    .fc-route-meta { margin-left: auto; font-size: 0.85rem; color: var(--muted); }
    .fc-hint { display: flex; align-items: center; gap: 0.45rem; margin: 0 0 0.75rem; font-size: 0.85rem; color: var(--primary-deep); }
    .fc-skeleton { height: 16rem; border-radius: var(--radius-lg); margin-top: 1.25rem; background: linear-gradient(90deg, var(--surface-tint), var(--surface), var(--surface-tint));
      background-size: 200% 100%; animation: fc-shimmer 1.2s linear infinite; }
    @keyframes fc-shimmer { to { background-position: -200% 0; } }
    .fc-note { margin-top: 1rem; }
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
      </form>

      <app-error-banner [error]="error()" />

      @if (loading()) {
        <p class="muted" aria-live="polite" style="margin-top: 1.25rem"><span class="spinner" aria-hidden="true"></span> {{ 'fcal.loading' | t }}</p>
        <div class="fc-skeleton"></div>
      } @else if (calendar(); as cal) {
        @if (!cal.available) {
          <p class="card muted" style="margin-top: 1.25rem">{{ 'fcal.unavailable' | t }}</p>
        } @else {
          <div class="fc-routes">
            @for (route of cal.routes; track route.fromCode + route.toCode) {
              <section class="card" [attr.aria-label]="routeName(route)">
                <div class="fc-route-head">
                  <h2>{{ routeName(route) }}</h2>
                  @if (route.fromKm != null) { <span class="fc-chip">{{ 'fcal.nearbyFrom' | t: { km: route.fromKm, city: requestedFrom() } }}</span> }
                  @if (route.toKm != null) { <span class="fc-chip">{{ 'fcal.nearbyTo' | t: { km: route.toKm, city: requestedTo() } }}</span> }
                  @if (route.days.length) {
                    <span class="fc-route-meta">{{ 'fcal.summary' | t: { count: route.days.length } }} ·
                      {{ 'fcal.priceFrom' | t: { price: (cheapest(route) | money: cal.currency) } }}</span>
                  }
                </div>
                @if (route.days.length) {
                  <p class="fc-hint"><app-icon name="sparkles" [size]="14" /> {{ 'fcal.clickHint' | t }}</p>
                  <app-fare-months [days]="route.days" [currency]="cal.currency" [tips]="true" [legend]="false" [faresOnly]="true"
                                   [selected]="picked()?.fromCode === route.fromCode && picked()?.toCode === route.toCode ? picked()!.fare.date : null"
                                   (picked)="pickDay(route, $event)" />
                } @else {
                  <p class="muted">
                    {{ 'fcal.none' | t: { route: routeName(route) } }}
                    @if (cal.routes.length > 1) { {{ 'fcal.noneNearby' | t }} }
                  </p>
                }
              </section>
            }
          </div>
          <p class="small muted fc-note">{{ 'fcal.note' | t }}</p>
        }
      }
    </div>

    <app-trip-dialog [flight]="picked()" (closed)="picked.set(null)" />
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
  protected readonly calendar = signal<FlightCalendarDto | null>(null);
  /** The day clicked on a calendar; opens the trip dialog. */
  protected readonly picked = signal<PickedFlight | null>(null);
  protected readonly currencies = computed(() => this.config.config()?.supportedCurrencies ?? ['EUR']);
  /** The names asked for, so a nearby route can say what it is near to. */
  protected readonly requestedFrom = signal('');
  protected readonly requestedTo = signal('');

  private readonly fromTerms = new Subject<string>();
  private readonly toTerms = new Subject<string>();
  private request?: Subscription;

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
    this.destroyRef.onDestroy(() => this.request?.unsubscribe());

    // Shareable links: ?from=SAN&fromName=…&to=BUE&toName=…&currency=EUR searches straight away.
    const q = this.route.snapshot.queryParamMap;
    const code = (key: string) => (/^[A-Za-z]{3}$/.test(q.get(key) ?? '') ? q.get(key)!.toUpperCase() : null);
    if (code('from')) this.setFrom({ code: code('from')!, label: q.get('fromName') ?? code('from')! });
    if (code('to')) this.setTo({ code: code('to')!, label: q.get('toName') ?? code('to')! });
    if (q.get('currency')) this.currency.set(q.get('currency')!.toUpperCase());
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

  protected setCurrency(value: string): void {
    this.currency.set(value);
    if (this.calendar()) this.search();
  }

  protected search(): void {
    const from = this.from();
    const to = this.to();
    if (!from || !to) return;
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.picked.set(null);
    this.requestedFrom.set(from.label);
    this.requestedTo.set(to.label);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { from: from.code, fromName: from.label, to: to.code, toName: to.label, currency: this.currency() },
      replaceUrl: true,
    });
    this.request = this.api.getFlightCalendar(from.code, to.code, this.currency()).subscribe({
      next: (r) => {
        this.calendar.set(r);
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(toApiError(e));
        this.calendar.set(null);
        this.loading.set(false);
      },
    });
  }

  /** A day with a fare opens the trip dialog; a day without one has nothing to show yet. */
  protected pickDay(route: FlightCalendarRouteDto, pick: DayPick): void {
    if (!pick.fare) return;
    const [fromName, toName] = this.routeName(route).split(' → ');
    this.picked.set({ fromCode: route.fromCode, toCode: route.toCode, fromName, toName, currency: this.calendar()!.currency, fare: pick.fare });
  }

  /** The route asked for keeps the names the visitor picked; nearby ones use the city names from the API. */
  protected routeName(route: FlightCalendarRouteDto): string {
    const from = route.fromKm == null ? this.requestedFrom() : `${route.fromCity} (${route.fromCode})`;
    const to = route.toKm == null ? this.requestedTo() : `${route.toCity} (${route.toCode})`;
    return `${from} → ${to}`;
  }

  protected cheapest(route: FlightCalendarRouteDto): number {
    return Math.min(...route.days.map((d) => d.price));
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
