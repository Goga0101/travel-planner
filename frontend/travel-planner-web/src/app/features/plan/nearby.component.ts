import { HotelSceneComponent, RouteHintComponent } from './stay-illustrations.component';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiService } from '../../core/api.service';
import { ApiError, toApiError } from '../../core/errors';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { HotelPriceDto, HotelPricesDto, NearbyPlacesDto, TransitMode, TripPlanSummaryDto } from '../../core/models';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { SelectedStayService, StayPrice, sitePrice, typicalPrice } from '../../core/selected-stay.service';
import { IconComponent, IconName } from '../../shared/icon.component';
import { DistancePipe, LocalDatePipe, MoneyPipe } from '../../shared/pipes';
import { HotelFinderComponent } from './hotel-finder.component';
import { StayBudgetComponent, nightlyAllowance } from './stay-budget.component';

type Area = 'prices' | 'airport' | 'centre';

const HOTEL_KINDS = ['hotel', 'hostel', 'guest_house', 'motel', 'apartment'];
const MODE_KEYS: Record<TransitMode, TranslationKey> = {
  bus: 'transit.bus',
  trolleybus: 'transit.trolleybus',
  train: 'transit.train',
  subway: 'transit.subway',
  tram: 'transit.tram',
  light_rail: 'transit.light_rail',
  monorail: 'transit.monorail',
};

/**
 * Hotels around the arrival airport / city centre and the ways to get there: public-transport lines serving the airport,
 * taxis, and pre-filled Booking.com, Google Maps and Uber links. Prices are only ever shown on the provider sites.
 */
@Component({
  selector: 'app-nearby',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HotelSceneComponent, RouteHintComponent, TranslatePipe, IconComponent, DistancePipe, LocalDatePipe, MoneyPipe, ErrorBannerComponent, HotelFinderComponent, StayBudgetComponent],
  templateUrl: './nearby.component.html',
})
export class NearbyComponent {
  readonly planId = input.required<string>();
  /** Which section to show: the hotels, the airport's transport (getting around), or both. */
  readonly part = input<'stay' | 'around' | 'all'>('all');
  /** Emitted when the visitor marks a hotel (not when unmarking), so the page can show the route. */
  readonly stayChosen = output<void>();

  protected readonly stays = inject(SelectedStayService);

  /** Marks the hotel the visitor plans to book; the route and the day plan then start there. */
  protected selectStay(name: string, latitude: number, longitude: number, priced?: HotelPriceDto): void {
    const wasSelected = this.stays.isSelected(this.planId(), latitude, longitude);
    this.stays.toggle(this.planId(), { name, latitude, longitude, price: this.stayPrice(priced) });
    if (!wasSelected) this.stayChosen.emit();
  }

  /** The hotel's published price (Booking.com first), or the city's typical price when the hotel has none here. */
  private stayPrice(priced?: HotelPriceDto): StayPrice | undefined {
    const hp = this.prices();
    if (!hp) return undefined;
    return priced ? sitePrice(hp, priced) : typicalPrice(hp);
  }

  protected hasViaTripAdvisor(h: HotelPriceDto): boolean {
    return h.rates.some((r) => r.viaTripAdvisor);
  }
  readonly summary = input.required<TripPlanSummaryDto>();

  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);

  protected readonly pageSize = 6;
  protected readonly data = signal<NearbyPlacesDto | null>(null);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly area = signal<Area>('airport');
  protected readonly prices = signal<HotelPricesDto | null>(null);
  /** What the traveller can pay for the stay (null = no limit) and whether that amount is per night. */
  protected readonly stayBudget = signal<number | null>(null);
  protected readonly budgetPerNight = signal(false);
  /**
   * The priced hotels within the budget (each at its cheapest site), best rated first; all of them, cheapest first, without
   * a budget.
   */
  protected readonly pricedHotels = computed(() => {
    const hp = this.prices();
    if (!hp) return [];
    const budget = this.stayBudget();
    if (budget === null) return hp.hotels;
    const limit = nightlyAllowance(budget, this.budgetPerNight(), hp.nights, hp.rooms);
    return hp.hotels.filter((h) => h.lowestNightly <= limit)
      .sort((a, b) => (b.rating ?? 0) - (a.rating ?? 0) || b.lowestNightly - a.lowestNightly);
  });
  protected readonly cheapestStay = computed(() => {
    const hp = this.prices();
    return hp?.hotels.length ? Math.min(...hp.hotels.map((h) => h.lowestNightly)) * hp.nights * Math.max(1, hp.rooms) : 0;
  });
  protected readonly pricesLoading = signal(false);
  protected readonly showAll = signal(false);
  protected readonly showAllLines = signal(false);
  protected readonly linesPageSize = 6;
  protected readonly visibleLines = computed(() => {
    const lines = this.data()?.transitLines ?? [];
    return this.showAllLines() ? lines : lines.slice(0, this.linesPageSize);
  });

  protected readonly hotels = computed(() => {
    const d = this.data();
    if (!d) return [];
    return this.area() === 'airport' ? d.airportHotels : d.centreHotels;
  });
  protected readonly visibleHotels = computed(() => (this.showAll() ? this.hotels() : this.hotels().slice(0, this.pageSize)));
  protected readonly city = computed(() => this.summary().destination.city);

  constructor() {
    // A budget is about the priced hotels: show that list.
    effect(() => {
      if (this.stayBudget() !== null && untracked(() => this.prices()?.hotels.length)) untracked(() => this.area.set('prices'));
    });
    // Reload when the plan or the language changes (names and links are localized by the API).
    effect(() => {
      this.planId();
      this.i18n.lang();
      untracked(() => this.load());
    });
  }

  load(): void {
    this.error.set(null);
    this.loadPrices();
    this.api
      .getNearbyPlaces(this.planId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => {
          this.data.set(d);
          // Airports far from any hotel: start on the city-centre tab instead of an empty list.
          if (this.area() === 'airport' && !d.airportHotels.length && d.centreHotels.length) this.area.set('centre');
        },
        error: (e) => this.error.set(toApiError(e)),
      });
  }

  /** Indicative prices for the plan dates; when available they become the first tab. */
  private loadPrices(): void {
    this.pricesLoading.set(true);
    this.api
      .getHotelPrices(this.planId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (hp) => {
          this.prices.set(hp);
          this.pricesLoading.set(false);
          if (hp?.hotels.length && this.area() === 'airport') this.area.set('prices');
        },
        error: () => this.pricesLoading.set(false),
      });
  }

  protected setArea(area: Area): void {
    this.area.set(area);
    this.showAll.set(false);
  }

  protected starText(stars: number): string {
    return '★'.repeat(stars);
  }

  protected kindKey(kind: string): TranslationKey {
    return (HOTEL_KINDS.includes(kind) ? `hotelKind.${kind}` : 'hotelKind.hotel') as TranslationKey;
  }

  protected modeKey(mode: TransitMode): TranslationKey {
    return MODE_KEYS[mode] ?? 'transit.bus';
  }

  protected modeIcon(mode: TransitMode): IconName {
    return mode === 'bus' || mode === 'trolleybus' ? 'bus' : 'train';
  }

  protected telHref(phone: string): string {
    // OSM may list several numbers separated by ";": dial the first one.
    return 'tel:' + phone.split(';')[0].replace(/[^\d+]/g, '');
  }
}
