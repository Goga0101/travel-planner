import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription, catchError, of } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ApiError, toApiError } from '../../core/errors';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { JourneyDto, PlaceSuggestionDto, TaxiOptionsDto } from '../../core/models';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { ExternalLinkComponent } from '../../shared/external-link.component';
import { IconComponent, IconName } from '../../shared/icon.component';
import { DistancePipe, MoneyPipe } from '../../shared/pipes';
import { ItineraryComponent, routeTags } from '../plan/itinerary.component';
import { LiveMapComponent } from './live-map.component';
import { PlaceInputComponent } from './place-input.component';

type Section = 'map' | 'routes' | 'taxi';

const SECTIONS: { id: Section; icon: IconName; label: 'transit.secMap' | 'transit.secRoutes' | 'transit.secTaxi' }[] = [
  { id: 'map', icon: 'compass', label: 'transit.secMap' },
  { id: 'routes', icon: 'bus', label: 'transit.secRoutes' },
  { id: 'taxi', icon: 'car', label: 'transit.secTaxi' },
];

/**
 * Getting around anywhere: from any place to any place, the vehicles moving on the map right now, the best public-transport
 * journeys with their tickets, and the local taxi apps and numbers — each in its own section.
 */
@Component({
  selector: 'app-transit-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, ExternalLinkComponent, ErrorBannerComponent, DistancePipe, MoneyPipe, ItineraryComponent,
    LiveMapComponent, PlaceInputComponent],
  template: `
    <div class="container page transit-page">
      <header class="transit-head">
        <p class="eyebrow"><span class="live-pulse" aria-hidden="true"></span> {{ 'transit.eyebrow' | t }}</p>
        <h1 class="page-title">{{ 'transit.title' | t }}</h1>
        <p class="muted">{{ 'transit.lead' | t }}</p>
      </header>

      <form class="card transit-search" (submit)="$event.preventDefault(); search()">
        <div class="transit-fields">
          <app-place-input #fromInput inputId="transit-from" label="transit.from" placeholder="transit.fromPlaceholder" end="from" [gps]="true"
                           [near]="to() ?? region()" [(value)]="from" />
          <button type="button" class="swap-btn" (click)="swap()" [title]="'transit.swap' | t" [attr.aria-label]="'transit.swap' | t">
            <app-icon name="shuffle" [size]="18" />
          </button>
          <app-place-input #toInput inputId="transit-to" label="transit.to" placeholder="transit.toPlaceholder" end="to" [near]="from() ?? region()" [(value)]="to" />
        </div>
        <div class="transit-when">
          <div class="pill-group" role="radiogroup" [attr.aria-label]="'transit.when' | t">
            <button type="button" role="radio" class="pill-btn" [class.active]="!later()" [attr.aria-checked]="!later()" (click)="later.set(false)">
              {{ 'transit.now' | t }}
            </button>
            <button type="button" role="radio" class="pill-btn" [class.active]="later()" [attr.aria-checked]="later()" (click)="later.set(true)">
              {{ 'transit.later' | t }}
            </button>
          </div>
          @if (later()) {
            <input type="datetime-local" class="input transit-time" [value]="time()" (input)="time.set($any($event.target).value)"
                   [attr.aria-label]="'transit.leaveAt' | t" />
          }
          <button type="submit" class="btn btn-primary transit-go" [disabled]="!from() || !to() || loading()">
            @if (loading()) { <span class="spinner" aria-hidden="true"></span> } @else { <app-icon name="search" [size]="18" /> }
            {{ 'transit.search' | t }}
          </button>
        </div>
        @if (hint()) { <p class="small field-error">{{ 'transit.pickBoth' | t }}</p> }
      </form>

      <app-error-banner [error]="error()" />

      @if (searchedFrom()) {
        <nav class="transit-sections" [attr.aria-label]="'transit.sections' | t">
          @for (s of sections; track s.id) {
            <a class="section-chip" [href]="'#sec-' + s.id" (click)="$event.preventDefault(); go(s.id)">
              <app-icon [name]="s.icon" [size]="16" /> <span class="section-label">{{ s.label | t }}</span>
              @if (s.id === 'routes' && journey()) { <span class="chip-count">{{ journey()!.direction.itineraries.length }}</span> }
              @if (s.id === 'taxi' && taxis()) { <span class="chip-count">{{ taxis()!.apps.length + taxis()!.local.length }}</span> }
            </a>
          }
        </nav>

        <section class="card transit-section" id="sec-map" aria-labelledby="sec-map-title">
          <div class="section-head">
            <h2 id="sec-map-title"><app-icon name="compass" /> {{ 'transit.secMap' | t }}</h2>
            <span class="small muted">{{ 'transit.mapNote' | t }}</span>
          </div>
          @if (stationRoute(); as sr) {
            <p class="small map-to-station"><app-icon name="bus" [size]="14" /> {{ 'transit.mapToStation' | t: { station: sr.station.name } }}</p>
          }
          <app-live-map [from]="searchedFrom()" [to]="mapTo()" [lines]="myLines()" [route]="mapRoute()" />
        </section>

        <section class="card transit-section" id="sec-routes" aria-labelledby="sec-routes-title">
          <div class="section-head">
            <h2 id="sec-routes-title"><app-icon name="bus" /> {{ 'transit.secRoutes' | t }}</h2>
            @if (journey(); as j) { <span class="small muted">{{ j.distanceMeters | distance }} · {{ 'transit.leaveAround' | t: { time: j.direction.wishedTime } }}</span> }
          </div>
          @if (loading() && !journey()) {
            <p class="muted"><span class="spinner" aria-hidden="true"></span> {{ 'transit.routesLoading' | t }}</p>
          } @else if (journey(); as j) {
            @let r = j.direction;
            @if (r.itineraries.length) {
              @if (!r.onTravelDate) { <p class="small muted">{{ 'transit.timetableOther' | t }}</p> }
              <p class="small route-pick-hint"><app-icon name="pin" [size]="14" /> {{ 'transit.pickRoute' | t }}</p>
              <ol class="route-options">
                @for (it of r.itineraries; track $index; let first = $first; let i = $index) {
                  <li app-itinerary class="route-option is-pickable" [class.best]="first" [class.is-picked]="picked() === i" [itinerary]="it" [best]="first" [tags]="mainTags()[i]"
                      tabindex="0" [attr.aria-current]="picked() === i" (click)="pick(i, $event)" (keydown.enter)="pick(i, $event)"></li>
                }
              </ol>
            } @else {
              <p class="muted">{{ (j.coaches.length ? 'transit.noRoutesCoach' : 'transit.noRoutes') | t }}</p>
            }
            @if (j.coaches.length) {
              <div class="coach-guide">
                <h3 class="sub-head"><app-icon name="bus" [size]="18" /> {{ 'transit.coachTitle' | t }}</h3>
                <p class="small muted">{{ 'transit.coachLead' | t }}</p>
                <ul class="coach-list">
                  @for (c of j.coaches; track c.name) {
                    <li class="coach-station" [class.is-recommended]="c.recommended">
                      <div class="coach-head">
                        <strong>{{ c.name }}</strong>
                        @if (c.recommended) { <span class="badge-best">{{ 'transit.coachBest' | t }}</span> }
                      </div>
                      <p class="small">{{ c.towns.join(' · ') }}</p>
                      @if (c.route?.itineraries?.length) {
                        <p class="coach-route-title"><app-icon name="metro" [size]="15" /> {{ 'transit.coachRouteTitle' | t }}</p>
                        <ol class="route-options">
                          @for (it of c.route!.itineraries; track $index; let first = $first; let i = $index) {
                            <li app-itinerary class="route-option is-pickable" [class.best]="first" [class.is-picked]="stationRoute() && stationPick() === i"
                                [itinerary]="it" [best]="first && !it.estimated" [tags]="stationTags()[i]" tabindex="0" (click)="pickStation(i, $event)" (keydown.enter)="pickStation(i, $event)"></li>
                          }
                        </ol>
                      }
                      <div class="coach-links">
                        <a class="btn btn-sm btn-deep" [href]="c.directionsUrl" target="_blank" rel="noopener noreferrer">
                          <app-icon name="metro" [size]="14" /> {{ 'transit.coachHowTo' | t }}
                        </a>
                        <app-external-link [href]="c.mapUrl" [label]="'days.map' | t" />
                      </div>
                    </li>
                  }
                </ul>
                <p class="small muted">{{ 'transit.coachNote' | t }}</p>
              </div>
            }
            @if (r.cityTicket; as tk) {
              <p class="leg-ticket" [class.online]="tk.buyOnline === true" [class.offline]="tk.buyOnline === false">
                <app-icon name="ticket" [size]="14" />
                <span>
                  <strong>{{ 'route.cityTickets' | t }}: {{ tk.name ?? ('route.ticket' | t) }}</strong>
                  @if (tk.price !== undefined && tk.price !== null) { · {{ tk.price | money: tk.currency }} }
                  @if (tk.buyOnline === true) { · {{ 'route.buyOnlineYes' | t }} }
                  @if (tk.note) { <br /><span class="muted">{{ tk.note }}</span> }
                </span>
                @if (tk.buyUrl) { <a class="leg-buy" [href]="tk.buyUrl" target="_blank" rel="noopener noreferrer nofollow">{{ 'route.buy' | t }} <app-icon name="external" [size]="12" /></a> }
              </p>
            }
            <div class="route-alt">
              <app-external-link [href]="r.googleTransitUrl" [label]="'route.googleTransit' | t" />
              <app-external-link [href]="r.googleDriveUrl" [label]="'route.drive' | t" />
            </div>
            @if (j.routerName) { <p class="small muted">{{ 'transit.routesSource' | t: { source: j.routerName } }}</p> }
          }
        </section>

        <section class="card transit-section" id="sec-taxi" aria-labelledby="sec-taxi-title">
          <div class="section-head">
            <h2 id="sec-taxi-title"><app-icon name="car" /> {{ 'transit.secTaxi' | t }}</h2>
          </div>
          @if (taxisLoading() && !taxis()) {
            <p class="muted"><span class="spinner" aria-hidden="true"></span> {{ 'transit.taxiLoading' | t }}</p>
          } @else if (taxis(); as tx) {
            <h3 class="sub-head">{{ 'transit.taxiApps' | t }}</h3>
            <ul class="taxi-apps">
              @for (a of tx.apps; track a.name) {
                <li class="taxi-app">
                  <span class="taxi-logo" aria-hidden="true">{{ a.name.charAt(0) }}</span>
                  <strong>{{ a.name }}</strong>
                  <div class="taxi-app-links">
                    @if (a.rideUrl) {
                      <a class="btn btn-sm btn-deep" [href]="a.rideUrl" target="_blank" rel="noopener noreferrer nofollow">
                        <app-icon name="car" [size]="14" /> {{ 'transit.orderRide' | t }}
                      </a>
                    }
                    <app-external-link [href]="a.url" [label]="'transit.openApp' | t" />
                  </div>
                </li>
              }
            </ul>
            <p class="small muted">{{ 'transit.taxiAppsNote' | t }}</p>

            <h3 class="sub-head">{{ 'transit.taxiLocal' | t }}</h3>
            @if (tx.local.length) {
              <ul class="taxi-local">
                @for (t of tx.local.slice(0, 12); track $index) {
                  <li>
                    <span class="taxi-icon"><app-icon [name]="t.phone ? 'phone' : 'pin'" [size]="16" /></span>
                    <span class="taxi-text">
                      <strong>{{ t.name ?? t.operator ?? ((t.isCompany ? 'transit.taxiCompany' : 'transit.taxiStand') | t) }}</strong>
                      <span class="small muted">{{ (t.isCompany ? 'transit.taxiCompany' : 'transit.taxiStand') | t }} · {{ t.distanceMeters | distance }}</span>
                    </span>
                    <span class="taxi-actions">
                      @if (t.phone) { <a class="btn btn-sm" [href]="'tel:' + tel(t.phone)"><app-icon name="phone" [size]="14" /> {{ t.phone }}</a> }
                      @if (t.website) { <app-external-link [href]="t.website" [label]="'days.website' | t" /> }
                      <app-external-link [href]="t.mapUrl" [label]="'days.map' | t" />
                    </span>
                  </li>
                }
              </ul>
            } @else {
              <p class="small muted">{{ 'transit.taxiNoLocal' | t }}</p>
            }
            <a class="btn btn-secondary btn-sm" [href]="tx.searchUrl" target="_blank" rel="noopener noreferrer">
              <app-icon name="search" [size]="14" /> {{ 'transit.taxiSearch' | t }}
            </a>
            <p class="small muted">{{ 'transit.taxiSource' | t: { source: tx.source } }}</p>
          }
        </section>
      } @else {
        <ul class="transit-intro">
          <li><span class="intro-icon"><app-icon name="compass" [size]="22" /></span><div><strong>{{ 'transit.secMap' | t }}</strong><p class="small muted">{{ 'transit.introMap' | t }}</p></div></li>
          <li><span class="intro-icon"><app-icon name="bus" [size]="22" /></span><div><strong>{{ 'transit.secRoutes' | t }}</strong><p class="small muted">{{ 'transit.introRoutes' | t }}</p></div></li>
          <li><span class="intro-icon"><app-icon name="car" [size]="22" /></span><div><strong>{{ 'transit.secTaxi' | t }}</strong><p class="small muted">{{ 'transit.introTaxi' | t }}</p></div></li>
        </ul>
      }
    </div>
  `,
})
export class TransitPage {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fromInput = viewChild.required<PlaceInputComponent>('fromInput');
  private readonly toInput = viewChild.required<PlaceInputComponent>('toInput');

  protected readonly sections = SECTIONS;
  protected readonly from = signal<PlaceSuggestionDto | null>(null);
  protected readonly to = signal<PlaceSuggestionDto | null>(null);
  protected readonly later = signal(false);
  protected readonly time = signal(localNow());
  protected readonly hint = signal(false);
  /** Roughly where the visitor is (nearest airport by IP): suggestions from their own city come first. */
  protected readonly region = signal<{ latitude: number; longitude: number } | null>(null);

  protected readonly searchedFrom = signal<PlaceSuggestionDto | null>(null);
  protected readonly searchedTo = signal<PlaceSuggestionDto | null>(null);
  protected readonly journey = signal<JourneyDto | null>(null);
  protected readonly taxis = signal<TaxiOptionsDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly taxisLoading = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  private subs: Subscription[] = [];

  /** Lines of the suggested journeys, highlighted on the map. */
  /** The journey drawn on the map (the fastest until the visitor taps another). */
  protected readonly picked = signal(0);
  protected readonly pickedRoute = computed(() => this.journey()?.direction.itineraries[this.picked()] ?? null);

  /** No direct timetable connection but a minibus station for the direction: the map shows the way to the station instead. */
  protected readonly stationRoute = computed(() => {
    const j = this.journey();
    if (!j || j.direction.itineraries.length) return null;
    const station = j.coaches.find((c) => c.recommended && c.route?.itineraries.length);
    if (!station) return null;
    const place: PlaceSuggestionDto = { name: station.name, latitude: station.latitude, longitude: station.longitude, kind: 'stop' };
    return { station, place };
  });
  protected readonly stationPick = signal(0);
  protected readonly mainTags = computed(() => routeTags(this.journey()?.direction.itineraries ?? []));
  protected readonly stationTags = computed(() => routeTags(this.journey()?.coaches.find((c) => c.recommended)?.route?.itineraries ?? []));
  protected readonly mapRoute = computed(() => {
    const sr = this.stationRoute();
    return sr ? sr.station.route!.itineraries[this.stationPick()] ?? null : this.pickedRoute();
  });
  protected readonly mapTo = computed(() => this.stationRoute()?.place ?? this.searchedTo());

  protected pickStation(i: number, e: Event): void {
    this.stationPick.set(i);
    if (!(e.target as Element | null)?.closest('a')) this.go('map');
  }

  protected pick(i: number, e: Event): void {
    this.picked.set(i);
    // A tap on a link inside the card (ticket, Google Maps) opens it; anywhere else shows the route on the map.
    if (!(e.target as Element | null)?.closest('a')) this.go('map');
  }

  protected readonly myLines = computed(() => {
    const lines = new Set<string>();
    for (const it of this.journey()?.direction.itineraries ?? []) for (const l of it.legs) if (l.mode !== 'WALK' && l.line) lines.add(l.line);
    return [...lines];
  });

  constructor() {
    this.destroyRef.onDestroy(() => this.subs.forEach((s) => s.unsubscribe()));
    this.api
      .getNearestAirport()
      .pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe((a) => a && this.region.set({ latitude: a.latitude, longitude: a.longitude }));
  }

  protected swap(): void {
    const from = this.from();
    const to = this.to();
    this.fromInput().set(to);
    this.toInput().set(from);
  }

  protected search(): void {
    const from = this.from();
    const to = this.to();
    if (!from || !to) {
      this.hint.set(true);
      return;
    }
    this.hint.set(false);
    this.subs.forEach((s) => s.unsubscribe());
    this.error.set(null);
    this.searchedFrom.set(from);
    this.searchedTo.set(to);
    this.journey.set(null);
    this.picked.set(0);
    this.stationPick.set(0);
    this.taxis.set(null);
    this.loading.set(true);
    this.taxisLoading.set(true);
    this.subs = [
      this.api
        .getJourney(from, to, this.later() && this.time() ? this.time() : undefined)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (j) => {
            this.journey.set(j);
            this.loading.set(false);
          },
          error: (e) => {
            this.error.set(toApiError(e));
            this.loading.set(false);
          },
        }),
      this.api
        .getTaxis(from, to)
        .pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef))
        .subscribe((t) => {
          this.taxis.set(t);
          this.taxisLoading.set(false);
        }),
    ];
    setTimeout(() => this.go('map'), 50);
  }

  protected go(id: Section): void {
    document.getElementById('sec-' + id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  protected tel(phone: string): string {
    return phone.split(/[;,]/)[0].replace(/[^\d+]/g, '');
  }
}

function localNow(): string {
  const d = new Date(Date.now() - new Date().getTimezoneOffset() * 60000);
  return d.toISOString().slice(0, 16);
}
