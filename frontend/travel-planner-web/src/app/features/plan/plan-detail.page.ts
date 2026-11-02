import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { combineLatest, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ClientConfigService } from '../../core/client-config.service';
import { ApiError, toApiError } from '../../core/errors';
import {
  OutboundLinkDto,
  PlanItemDto,
  TransferMode,
  TripPlanDetailDto,
  accommodationDetails,
  flightDetails,
  transferDetails,
} from '../../core/models';
import { BudgetFitBadgeComponent, VerificationBadgeComponent, VerificationLegendComponent } from '../../shared/badges';
import { CategoryPipe } from '../../core/i18n/category.pipe';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { AirlineComponent } from '../../shared/airline.component';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { ExternalLinkComponent } from '../../shared/external-link.component';
import { IconComponent, IconName } from '../../shared/icon.component';
import { SelectedStayService, findPriced, sitePrice, tripTotal, typicalPrice } from '../../core/selected-stay.service';
import { StayRouteComponent } from './stay-route.component';
import { destinationPhoto } from '../../core/destination-photos';
import { buildIcs, downloadFile } from '../../core/calendar';
import { SavedTripsService } from '../../core/local-lists';
import { AttractionsComponent } from './attractions.component';
import { NearbyComponent } from './nearby.component';
import { WeatherComponent } from './weather.component';
import { TripNewsComponent } from '../../shared/trip-news.component';
import { HotelReviewsComponent } from './hotel-reviews.component';
import { DayPlannerComponent } from './day-planner.component';
import { TripDiaryComponent } from './trip-diary.component';
import { FlightTicketsComponent, FlightTimelineComponent } from '../../shared/flight-route.component';
import { DistancePipe, LocalDatePipe, LocalNumberPipe, MoneyPipe, StopsPipe, TimeSpanPipe } from '../../shared/pipes';

/** Builds the Google Maps Embed directions URL from numbers only; returns null for invalid input. */
export function buildEmbedUrl(key: string, fromLat: number, fromLng: number, toLat: number, toLng: number): string | null {
  const nums = [fromLat, fromLng, toLat, toLng];
  if (!key || nums.some((n) => typeof n !== 'number' || !Number.isFinite(n))) return null;
  const p = (a: number, b: number) => `${a.toFixed(6)},${b.toFixed(6)}`;
  return (
    'https://www.google.com/maps/embed/v1/directions' +
    `?key=${encodeURIComponent(key)}&origin=${p(fromLat, fromLng)}&destination=${p(toLat, toLng)}&mode=transit`
  );
}

const TAB_IDS = ['overview', 'flight', 'stay', 'route', 'days', 'sights', 'costs'] as const;
type PlanTab = (typeof TAB_IDS)[number];

@Component({
  selector: 'app-plan-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AirlineComponent,
    LocalDatePipe,
    LocalNumberPipe,
    TranslatePipe,
    CategoryPipe,
    RouterLink,
    MoneyPipe,
    TimeSpanPipe,
    DistancePipe,
    StopsPipe,
    ExternalLinkComponent,
    VerificationBadgeComponent,
    BudgetFitBadgeComponent,
    VerificationLegendComponent,
    ErrorBannerComponent,
    IconComponent,
    NearbyComponent,
    AttractionsComponent,
    WeatherComponent,
    TripNewsComponent,
    HotelReviewsComponent,
    FlightTicketsComponent,
    FlightTimelineComponent,
    DayPlannerComponent,
    StayRouteComponent,
    TripDiaryComponent,
  ],
  templateUrl: './plan-detail.page.html',
})
export class PlanDetailPage {
  readonly id = input.required<string>();
  /** ?tab= query parameter (component input binding); the overview when absent. */
  readonly tab = input<string | undefined>();

  private readonly router = inject(Router);
  protected readonly stays = inject(SelectedStayService);

  protected readonly activeTab = computed<PlanTab>(() => (TAB_IDS as readonly string[]).includes(this.tab() ?? '') ? (this.tab() as PlanTab) : 'overview');

  /** Sidebar entries with live hints (flight times, chosen hotel, totals). */
  protected readonly tabs = computed(() => {
    const p = this.plan();
    this.stays.all();
    const lang = this.i18n.lang();
    void lang;
    const s = p?.summary;
    const stay = p ? this.stays.get(p.summary.id) : null;
    const out = s?.flight?.outbound;
    const t = (key: TranslationKey, params?: Record<string, string | number>) => this.i18n.t(key, params);
    const money = (v: number) => new Intl.NumberFormat(lang === 'ka' ? 'ka-GE' : 'en-GB', { style: 'currency', currency: s?.currency ?? 'EUR', maximumFractionDigits: 0 }).format(v);
    const rows: { id: PlanTab; icon: IconName; label: TranslationKey; hint: string; description: TranslationKey }[] = [
      { id: 'overview', icon: 'layout', label: 'tab.overview', hint: t('tab.overviewHint'), description: 'tab.overviewHint' },
      { id: 'flight', icon: 'plane', label: 'tab.flight', description: 'tab.flightDesc',
        hint: out ? `${out.from} → ${out.to} · ${out.departureLocal.slice(11, 16)}` : t('tab.flightHint') },
      { id: 'stay', icon: 'bed', label: 'tab.stay', description: 'tab.stayDesc',
        hint: stay ? t('tab.stayChosen', { name: stay.name }) : t('tab.stayHint') },
      { id: 'route', icon: 'metro', label: 'tab.route', description: 'tab.routeDesc',
        hint: t('tab.routeHint', { from: out?.to ?? '✈', to: stay?.name ?? t('tab.centre') }) },
      { id: 'days', icon: 'calendar', label: 'tab.days', description: 'tab.daysDesc', hint: t('tab.daysHint', { count: (s?.nights ?? 0) + 1 }) },
      { id: 'sights', icon: 'landmark', label: 'tab.sights', description: 'tab.sightsDesc', hint: t('tab.sightsHint') },
      { id: 'costs', icon: 'wallet', label: 'tab.costs', description: 'tab.costsDesc',
        hint: s ? t('tab.costsHint', { total: money(this.totals()!.total), left: money(Math.max(0, this.totals()!.remaining)) }) : '' },
    ];
    return rows.filter((r) => r.id !== 'flight' || !!this.flight());
  });

  protected readonly nextTab = computed(() => {
    const list = this.tabs();
    const i = list.findIndex((t) => t.id === this.activeTab());
    return i >= 0 && i < list.length - 1 ? list[i + 1] : null;
  });

  protected setTab(id: PlanTab): void {
    void this.router.navigate([], { queryParams: { tab: id === 'overview' ? null : id }, queryParamsHandling: 'merge' });
    if (typeof window !== 'undefined') window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  private readonly api = inject(ApiService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly destroyRef = inject(DestroyRef);
  private readonly configService = inject(ClientConfigService);
  protected readonly i18n = inject(I18nService);
  private readonly savedTrips = inject(SavedTripsService);

  protected readonly plan = signal<TripPlanDetailDto | null>(null);
  protected readonly photo = computed(() => destinationPhoto(this.plan()?.destination.code));
  protected readonly links = signal<OutboundLinkDto[]>([]);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly refreshing = signal(false);
  protected readonly refreshError = signal<ApiError | null>(null);
  protected readonly actionStatus = signal('');
  protected readonly isSaved = computed(() => {
    const p = this.plan();
    this.savedTrips.trips();
    return !!p && this.savedTrips.isSaved(p.summary.id);
  });

  protected readonly summary = computed(() => this.plan()?.summary ?? null);
  /**
   * The one total the whole page shows: the plan's known costs plus the estimate of the hotel the visitor picked (kept
   * apart, so the budget stays honest about what is a provider price and what is an estimate).
   */
  protected readonly totals = computed(() => {
    const s = this.plan()?.summary;
    this.stays.all();
    return s ? tripTotal(s.totalEstimatedCost, s.budgetAmount, s.currency, this.stays.get(s.id)) : null;
  });
  /** Missing cost categories, without the stay once a picked hotel's estimate covers it. */
  protected readonly missingCosts = computed(() =>
    (this.plan()?.summary.missingCostCategories ?? []).filter((c) => c !== 'Accommodation' || !this.totals()?.stay));
  /** Share of the budget spent, per cost category, then the hotel estimate (capped at 100% overall). */
  protected readonly budgetBar = computed(() => {
    const p = this.plan();
    const tt = this.totals();
    if (!p || !tt || p.summary.budgetAmount <= 0) return [];
    const scale = Math.max(p.summary.budgetAmount, tt.total);
    const rows = p.costBreakdown.filter((c) => c.amount > 0)
      .map((c) => ({ category: c.category, amount: c.amount, percent: (c.amount / scale) * 100, estimate: false }));
    if (tt.stay) rows.push({ category: 'Accommodation', amount: tt.stay.total, percent: (tt.stay.total / scale) * 100, estimate: true });
    return rows;
  });
  protected readonly budgetUsedPercent = computed(() => {
    const s = this.plan()?.summary;
    const tt = this.totals();
    return s && tt && s.budgetAmount > 0 ? Math.round((tt.total / s.budgetAmount) * 1000) / 10 : 0;
  });
  protected readonly flight = computed(() => this.item('Flight'));
  protected readonly flightInfo = computed(() => (this.flight() ? flightDetails(this.flight()!) : null));
  protected readonly accommodation = computed(() => this.item('Accommodation'));
  protected readonly accommodationInfo = computed(() => {
    const a = this.accommodation();
    return a && a.verification !== 'Unavailable' ? accommodationDetails(a) : null;
  });
  protected readonly transfers = computed(() =>
    (this.plan()?.items ?? []).filter((i) => i.type === 'Transfer').map((i) => ({ item: i, details: transferDetails(i) })),
  );
  protected readonly linkGroups = computed(() => {
    const groups = new Map<string, OutboundLinkDto[]>();
    for (const l of this.links()) groups.set(l.kind, [...(groups.get(l.kind) ?? []), l]);
    return [...groups.entries()].map(([kind, items]) => ({ kind, items }));
  });

  protected readonly mapEmbedUrl = computed<SafeResourceUrl | null>(() => {
    const key = this.configService.config()?.mapsEmbedApiKey;
    const t = this.transfers()[0]?.details;
    if (!key || !t || t.accommodationLatitude === undefined || t.accommodationLongitude === undefined) return null;
    const url = buildEmbedUrl(key, t.airportLatitude, t.airportLongitude, t.accommodationLatitude, t.accommodationLongitude);
    // Safe: fixed https://www.google.com origin; every variable part is a number or URI-encoded key.
    return url ? this.sanitizer.bypassSecurityTrustResourceUrl(url) : null;
  });
  protected readonly mapsDirectionsUrl = computed(() => this.transfers()[0]?.details.mapsDirectionsUrl ?? null);

  constructor() {
    // Re-fetches on language change so the destination texts follow the selected language.
    combineLatest([toObservable(this.id), toObservable(this.i18n.lang)])
      .pipe(
        switchMap(([id]) => this.api.getTripPlan(id)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (p) => {
          this.plan.set(p);
          this.loadLinks();
          this.refreshStayPrice(p.summary.id);
        },
        error: (e) => this.error.set(toApiError(e)),
      });
  }

  protected refresh(): void {
    if (this.refreshing()) return;
    this.refreshing.set(true);
    this.refreshError.set(null);
    this.api.refreshPrices(this.id()).subscribe({
      next: (p) => {
        this.plan.set(p);
        this.refreshing.set(false);
        this.loadLinks();
      },
      error: (e) => {
        this.refreshError.set(toApiError(e));
        this.refreshing.set(false);
      },
    });
  }

  protected toggleSaved(): void {
    const p = this.plan();
    if (!p) return;
    const saved = this.savedTrips.toggle(p.summary);
    this.actionStatus.set(this.i18n.t(saved ? 'plan.savedStatus' : 'plan.removedStatus'));
  }

  /** Native share sheet where available (mobile), otherwise copies the plan link. */
  protected async share(): Promise<void> {
    const p = this.plan();
    if (!p) return;
    const url = location.href.split('#')[0];
    const title = `${p.destination.city} – ${this.i18n.t('app.brand')}`;
    try {
      if (navigator.share) {
        await navigator.share({ title, url });
        return;
      }
      await navigator.clipboard.writeText(url);
      this.actionStatus.set(this.i18n.t('plan.linkCopied'));
    } catch (e) {
      // AbortError = the user closed the share sheet; anything else: show the link so it can be copied by hand.
      if ((e as DOMException)?.name !== 'AbortError') this.actionStatus.set(url);
    }
  }

  protected addToCalendar(): void {
    const p = this.plan();
    const f = this.flightInfo();
    const item = this.flight();
    if (!p || !f || !item) return;
    const url = location.href.split('#')[0];
    const legs = [
      { key: 'out', it: f.outbound },
      { key: 'back', it: f.inbound },
    ];
    const events = legs.map(({ key, it }) => ({
      uid: `${p.summary.id}-${key}@travelvista`,
      title: this.i18n.t('plan.calendarFlight', { from: it.departureAirport, to: it.arrivalAirport }),
      start: it.departureLocal,
      end: it.arrivalLocal,
      location: it.departureAirport,
      description: [
        it.segments.map((sg) => `${sg.flightNumber} ${sg.departureAirport} → ${sg.arrivalAirport}`).join(', '),
        this.i18n.t('plan.calendarNote', { provider: item.provider }),
        url,
      ].filter(Boolean).join('\n'),
      url,
    }));
    downloadFile(`trip-${p.destination.code}-${p.summary.departureDate}.ics`, buildIcs(events), 'text/calendar;charset=utf-8');
    this.actionStatus.set(this.i18n.t('plan.calendarDone'));
  }

  protected print(): void {
    window.print();
  }

  protected directionKey(item: PlanItemDto): TranslationKey {
    return item.direction === 'AirportToAccommodation' ? 'plan.dirIn' : 'plan.dirOut';
  }

  protected transferModeKey(mode: TransferMode): TranslationKey {
    return `transferMode.${mode}`;
  }

  protected linkKindKey(kind: string): TranslationKey | null {
    return kind === 'Booking' || kind === 'Information' || kind === 'Map' ? `linkKind.${kind}` : null;
  }

  private item(type: PlanItemDto['type']): PlanItemDto | null {
    return this.plan()?.items.find((i) => i.type === type) ?? null;
  }

  /**
   * Fetches the hotel price list again (cached on the server for a few hours) and updates the picked hotel's price, so
   * the total follows what the booking sites publish now. Quietly keeps the old price when nothing comes back.
   */
  private refreshStayPrice(planId: string): void {
    if (!this.stays.get(planId)) return;
    this.api.getHotelPrices(planId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (hp) => {
        const stay = this.stays.get(planId);
        if (!hp || !stay) return;
        const hotel = findPriced(hp, stay);
        this.stays.setPrice(planId, hotel ? sitePrice(hp, hotel) : stay.price?.typical || !stay.price ? typicalPrice(hp) : stay.price);
      },
    });
  }

  private loadLinks(): void {
    this.api
      .getOutboundLinks(this.id())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (l) => this.links.set(l), error: () => this.links.set([]) });
  }
}
