import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ClientConfigService } from '../../core/client-config.service';
import { ApiError, toApiError } from '../../core/errors';
import { toIsoDate } from '../../core/format';
import { AirportDto, CreateTripSearchCommand, DepartureTimePreference, DestinationMode, DestinationSummaryDto } from '../../core/models';
import { switchMap } from 'rxjs';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService, TranslateParams } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { CTA_PHOTO, CTA_SRCSET, HERO_PHOTO, HERO_SRCSET, destinationPhoto, destinationSrcset } from '../../core/destination-photos';
import { RecentSearchesService } from '../../core/local-lists';
import { LocalDatePipe, MoneyPipe } from '../../shared/pipes';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { IconComponent, IconName } from '../../shared/icon.component';
import { DATE_ERROR_KEYS, manualDestinationsValidator, tripDatesValidator } from './trip-search.validators';

function defaultDates(today: Date): { from: string; to: string } {
  const year = today.getFullYear();
  const nov20 = new Date(year, 10, 20);
  if (nov20 > today) return { from: toIsoDate(nov20), to: toIsoDate(new Date(year, 10, 30)) };
  const from = new Date(today.getFullYear(), today.getMonth() + 1, 10);
  const to = new Date(from.getFullYear(), from.getMonth(), 20);
  return { from: toIsoDate(from), to: toIsoDate(to) };
}

@Component({
  selector: 'app-trip-search-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, ErrorBannerComponent, TranslatePipe, IconComponent, LocalDatePipe, MoneyPipe],
  templateUrl: './trip-search.page.html',
})
export class TripSearchPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  protected readonly recent = inject(RecentSearchesService);
  protected readonly configService = inject(ClientConfigService);
  protected readonly i18n = inject(I18nService);

  protected readonly airports = signal<AirportDto[]>([]);
  protected readonly destinations = signal<DestinationSummaryDto[]>([]);
  protected readonly catalogError = signal<ApiError | null>(null);
  protected readonly submitError = signal<ApiError | null>(null);
  protected readonly serverFieldErrors = signal<Record<string, string[]>>({});
  protected readonly submitting = signal(false);
  protected readonly submitted = signal(false);
  protected readonly destinationFilter = signal('');
  protected readonly departureTimes: DepartureTimePreference[] = ['Any', 'Morning', 'Afternoon', 'Evening', 'Night'];
  protected readonly showAll = signal(false);
  protected readonly announcement = signal('');

  protected readonly heroPhoto = HERO_PHOTO;
  protected readonly heroSrcset = HERO_SRCSET;
  protected readonly ctaPhoto = CTA_PHOTO;
  protected readonly ctaSrcset = CTA_SRCSET;
  protected readonly photo = destinationPhoto;
  protected readonly srcset = destinationSrcset;
  protected readonly featuredCode = 'lisbon';
  protected readonly featuredDays = 5;
  protected readonly featuredHighlights: TranslationKey[] = ['featured.h1', 'featured.h2', 'featured.h3'];
  protected readonly benefits: { key: string; icon: IconName; title: TranslationKey; text: TranslationKey }[] = [
    { key: 'compare', icon: 'scale', title: 'benefit.compare.title', text: 'benefit.compare.text' },
    { key: 'budget', icon: 'wallet', title: 'benefit.budget.title', text: 'benefit.budget.text' },
    { key: 'discover', icon: 'compass', title: 'benefit.discover.title', text: 'benefit.discover.text' },
    { key: 'flexible', icon: 'calendar', title: 'benefit.flexible.title', text: 'benefit.flexible.text' },
  ];
  protected readonly steps: { icon: IconName; title: TranslationKey; text: TranslationKey }[] = [
    { icon: 'sliders', title: 'step.1.title', text: 'step.1.text' },
    { icon: 'sparkles', title: 'step.2.title', text: 'step.2.text' },
    { icon: 'scale', title: 'step.3.title', text: 'step.3.text' },
    { icon: 'external', title: 'step.4.title', text: 'step.4.text' },
  ];

  private readonly dates = defaultDates(new Date());
  private readonly todayIso = () => toIsoDate(new Date());
  private readonly maxWindow = () => this.configService.config()?.maxSearchWindowDays ?? 60;
  private readonly maxDestinations = () => this.configService.config()?.maxManualDestinations ?? 10;

  protected readonly form = this.fb.nonNullable.group(
    {
      originAirportIata: ['TBS', [Validators.required, Validators.pattern(/^[A-Za-z]{3}$/)]],
      earliestDeparture: [this.dates.from, Validators.required],
      latestReturn: [this.dates.to, Validators.required],
      durationDays: [5, [Validators.required, Validators.min(2), Validators.max(30)]],
      travelers: [1, [Validators.required, Validators.min(1), Validators.max(9)]],
      budgetAmount: [1000, [Validators.required, Validators.min(1), Validators.max(1_000_000)]],
      budgetCurrency: ['EUR', Validators.required],
      destinationMode: ['Automatic' as DestinationMode],
      destinationIds: [[] as string[]],
      preferences: this.fb.nonNullable.group({
        includeCheckedBaggage: [false],
        minAccommodationStars: [null as number | null, [Validators.min(1), Validators.max(5)]],
        maxFlightDurationHours: [null as number | null, [Validators.min(1), Validators.max(48)]],
        maxStops: [null as number | null, [Validators.min(0), Validators.max(3)]],
        preferredDepartureTime: ['Any' as DepartureTimePreference],
        includeDailyExpenseEstimates: [false],
      }),
    },
    { validators: [tripDatesValidator(this.todayIso, this.maxWindow), manualDestinationsValidator(this.maxDestinations)] },
  );

  protected readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly isManual = computed(() => this.formValue().destinationMode === 'Manual');
  protected readonly nights = computed(() => Math.max(0, (this.formValue().durationDays ?? 0) - 1));
  protected readonly selectedIds = computed(() => this.formValue().destinationIds ?? []);
  protected readonly selectedDestinations = computed(() => {
    const ids = new Set(this.selectedIds());
    return this.destinations().filter((d) => ids.has(d.id));
  });
  protected readonly filteredDestinations = computed(() => {
    const term = this.destinationFilter().trim().toLowerCase();
    const ids = new Set(this.selectedIds());
    return this.destinations()
      .filter((d) => !ids.has(d.id))
      .filter((d) => !term || d.city.toLowerCase().includes(term) || d.country.toLowerCase().includes(term) || d.airportCodes.some((c) => c.toLowerCase() === term));
  });
  /** Catalog destinations with a photo, most popular first; the featured city is shown separately. */
  private readonly gallery = computed(() =>
    [...this.destinations()].filter((d) => d.code !== this.featuredCode && destinationPhoto(d.code)).sort((a, b) => b.popularity - a.popularity),
  );
  protected readonly popular = computed(() => this.gallery().slice(0, 4));
  protected readonly moreCount = computed(() => Math.max(0, this.gallery().length - 4));
  protected readonly more = computed(() => this.gallery().slice(4, this.showAll() ? undefined : 7));
  protected readonly featured = computed(() => this.destinations().find((d) => d.code === this.featuredCode));
  protected readonly originCity = computed(() => {
    const iata = this.formValue().originAirportIata;
    return this.airports().find((a) => a.iataCode === iata)?.city ?? iata;
  });
  protected readonly currencies = computed(() => this.configService.config()?.supportedCurrencies ?? ['EUR']);
  protected readonly maxManual = computed(() => this.configService.config()?.maxManualDestinations ?? 10);

  constructor() {
    // Airport and city names are localized by the API, so reload them when the language changes.
    const lang$ = toObservable(this.i18n.lang);
    lang$.pipe(switchMap(() => this.api.getDepartureAirports()), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (a) => this.airports.set(a),
      error: (e) => this.catalogError.set(toApiError(e)),
    });
    lang$.pipe(switchMap(() => this.api.getDestinations(undefined, 1, 100)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (page) => this.destinations.set(page.items),
      error: (e) => this.catalogError.set(toApiError(e)),
    });
    // Server-side field errors become stale as soon as the user edits the form.
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.serverFieldErrors.set({}));
  }

  protected setMode(mode: DestinationMode): void {
    this.form.controls.destinationMode.setValue(mode);
  }

  protected addDestination(id: string): void {
    const ids = this.form.controls.destinationIds.value;
    if (ids.includes(id) || ids.length >= this.maxManual()) return;
    this.form.controls.destinationIds.setValue([...ids, id]);
    this.destinationFilter.set('');
  }

  protected removeDestination(id: string): void {
    this.form.controls.destinationIds.setValue(this.form.controls.destinationIds.value.filter((x) => x !== id));
  }

  /** Destination cards preselect that city (manual mode) and bring the search panel into view. */
  protected pickDestination(d: DestinationSummaryDto): void {
    this.form.controls.destinationMode.setValue('Manual');
    this.form.controls.destinationIds.setValue([d.id]);
    this.announcement.set(this.i18n.t('search.cityPicked', { city: d.city }));
    this.startPlanning('duration');
  }

  protected pickFeatured(): void {
    this.form.controls.durationDays.setValue(this.featuredDays);
    const d = this.featured();
    if (d) this.pickDestination(d);
    else this.startPlanning();
  }

  protected showAllDestinations(event: Event): void {
    event.preventDefault();
    this.showAll.set(true);
    this.document.getElementById('more')?.scrollIntoView({ block: 'start' });
  }

  protected startPlanning(focusId = 'origin'): void {
    this.document.getElementById('trip-search')?.scrollIntoView({ block: 'start' });
    this.document.getElementById(focusId)?.focus({ preventScroll: true });
  }

  protected showError(path: string): boolean {
    const control = this.form.get(path);
    return !!control && control.invalid && (control.touched || this.submitted());
  }

  protected groupErrors(): string[] {
    if (!this.form.errors || !(this.submitted() || this.form.touched)) return [];
    const errors = this.form.errors;
    return Object.keys(errors).map((k) => {
      const key = DATE_ERROR_KEYS[k];
      const params = typeof errors[k] === 'object' ? (errors[k] as TranslateParams) : undefined;
      return key ? this.i18n.t(key, params) : k;
    });
  }

  protected departureTimeKey(t: DepartureTimePreference): TranslationKey {
    return `departureTime.${t}`;
  }

  protected serverErrors(field: string): string[] {
    return this.serverFieldErrors()[field] ?? [];
  }

  protected submit(): void {
    this.submitted.set(true);
    this.submitError.set(null);
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting()) return;

    const v = this.form.getRawValue();
    const command: CreateTripSearchCommand = {
      originAirportIata: v.originAirportIata.toUpperCase(),
      earliestDeparture: v.earliestDeparture,
      latestReturn: v.latestReturn,
      durationDays: Number(v.durationDays),
      travelers: Number(v.travelers),
      budgetAmount: Number(v.budgetAmount),
      budgetCurrency: v.budgetCurrency,
      destinationMode: v.destinationMode,
      destinationIds: v.destinationMode === 'Manual' ? v.destinationIds : null,
      preferences: {
        includeCheckedBaggage: v.preferences.includeCheckedBaggage,
        minAccommodationStars: numberOrNull(v.preferences.minAccommodationStars),
        maxFlightDurationHours: numberOrNull(v.preferences.maxFlightDurationHours),
        maxStops: numberOrNull(v.preferences.maxStops),
        preferredDepartureTime: v.preferences.preferredDepartureTime,
        includeDailyExpenseEstimates: v.preferences.includeDailyExpenseEstimates,
      },
    };

    this.submitting.set(true);
    this.api.createTripSearch(command).subscribe({
      next: (res) => {
        this.recent.add(res.id, command, v.destinationMode === 'Manual' ? this.selectedDestinations().map((d) => d.city) : []);
        void this.router.navigate(this.i18n.path('searches', res.id));
      },
      error: (e) => {
        const error = toApiError(e);
        this.submitError.set(error);
        this.serverFieldErrors.set(error.fieldErrors);
        this.submitting.set(false);
      },
    });
  }
}

function numberOrNull(value: number | string | null): number | null {
  if (value === null || value === '' || value === undefined) return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}
