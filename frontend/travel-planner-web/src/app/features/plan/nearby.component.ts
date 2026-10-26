import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiService } from '../../core/api.service';
import { ApiError, toApiError } from '../../core/errors';
import { formatLocalDate } from '../../core/format';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { NearbyPlacesDto, TransitMode, TripPlanSummaryDto } from '../../core/models';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { IconComponent, IconName } from '../../shared/icon.component';
import { DistancePipe, LocalDatePipe } from '../../shared/pipes';

type Area = 'airport' | 'centre';

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
  imports: [TranslatePipe, IconComponent, DistancePipe, LocalDatePipe, ErrorBannerComponent],
  templateUrl: './nearby.component.html',
})
export class NearbyComponent {
  readonly planId = input.required<string>();
  readonly summary = input.required<TripPlanSummaryDto>();

  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);

  protected readonly pageSize = 6;
  protected readonly data = signal<NearbyPlacesDto | null>(null);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly area = signal<Area>('airport');
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
  protected readonly travelers = computed(() => this.summary().travelers);
  protected readonly dates = computed(() => {
    const s = this.summary();
    const lang = this.i18n.lang();
    return `${formatLocalDate(s.departureDate, 'd MMM', lang)} – ${formatLocalDate(s.returnDate, 'd MMM', lang)}`;
  });

  constructor() {
    // Reload when the plan or the language changes (names and links are localized by the API).
    effect(() => {
      this.planId();
      this.i18n.lang();
      untracked(() => this.load());
    });
  }

  load(): void {
    this.error.set(null);
    this.api
      .getNearbyPlaces(this.planId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => {
          this.data.set(d);
          // Airports far from any hotel: start on the city-centre tab instead of an empty list.
          if (!d.airportHotels.length && d.centreHotels.length) this.area.set('centre');
        },
        error: (e) => this.error.set(toApiError(e)),
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
