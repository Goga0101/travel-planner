import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
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
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { ExternalLinkComponent } from '../../shared/external-link.component';
import { IconComponent } from '../../shared/icon.component';
import { destinationPhoto } from '../../core/destination-photos';
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

@Component({
  selector: 'app-plan-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
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
  ],
  templateUrl: './plan-detail.page.html',
})
export class PlanDetailPage {
  readonly id = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly destroyRef = inject(DestroyRef);
  private readonly configService = inject(ClientConfigService);
  protected readonly i18n = inject(I18nService);

  protected readonly plan = signal<TripPlanDetailDto | null>(null);
  protected readonly photo = computed(() => destinationPhoto(this.plan()?.destination.code));
  protected readonly links = signal<OutboundLinkDto[]>([]);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly refreshing = signal(false);
  protected readonly refreshError = signal<ApiError | null>(null);

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

  private loadLinks(): void {
    this.api
      .getOutboundLinks(this.id())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (l) => this.links.set(l), error: () => this.links.set([]) });
  }
}
