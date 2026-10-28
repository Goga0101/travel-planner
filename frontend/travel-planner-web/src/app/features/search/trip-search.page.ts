import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ClientConfigService } from '../../core/client-config.service';
import { ApiError, toApiError } from '../../core/errors';
import { toIsoDate } from '../../core/format';
import {
  AirportDto,
  CONTINENTS,
  Continent,
  CreateTripSearchCommand,
  DepartureTimePreference,
  DestinationMode,
  DestinationSummaryDto,
  PlaceDto,
} from '../../core/models';
import { Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService, TranslateParams } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { CTA_PHOTO, CTA_SRCSET, HERO_PHOTO, HERO_SRCSET, destinationPhoto, destinationSrcset } from '../../core/destination-photos';
import { RecentSearchesService } from '../../core/local-lists';
import { LocalDatePipe, MoneyPipe } from '../../shared/pipes';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { IconComponent, IconName } from '../../shared/icon.component';
import { DATE_ERROR_KEYS, manualDestinationsValidator, tripDatesValidator } from './trip-search.validators';

const ORIGIN_KEY = 'tp.origin';

/** The departure airport the visitor picked last (per browser); storage may be unavailable. */
function rememberedOrigin(): AirportDto | null {
  try {
    const raw = localStorage.getItem(ORIGIN_KEY);
    const value = raw ? (JSON.parse(raw) as AirportDto) : null;
    return value && /^[A-Z]{3}$/.test(value.iataCode) ? value : null;
  } catch {
    return null;
  }
}

function rememberOrigin(airport: AirportDto): void {
  try {
    localStorage.setItem(ORIGIN_KEY, JSON.stringify(airport));
  } catch {
    /* storage unavailable */
  }
}

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
  /** Selected departure airport and the departure combobox state. */
  protected readonly origin = signal<AirportDto | null>(rememberedOrigin());
  protected readonly originSuggested = signal(false);
  protected readonly originQuery = signal('');
  protected readonly originOptions = signal<AirportDto[]>([]);
  protected readonly originOpen = signal(false);
  protected readonly originActive = signal(-1);
  private readonly originTerms = new Subject<string>();
  /** World cities (outside the catalog) matching the destination filter, and the ones picked. */
  protected readonly worldMatches = signal<PlaceDto[]>([]);
  protected readonly pickedPlaces = signal<PlaceDto[]>([]);
  private readonly placeTerms = new Subject<string>();
  protected readonly continents = CONTINENTS;
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
      originAirportIata: [rememberedOrigin()?.iataCode ?? 'TBS', [Validators.required, Validators.pattern(/^[A-Za-z]{3}$/)]],
      earliestDeparture: [this.dates.from, Validators.required],
      latestReturn: [this.dates.to, Validators.required],
      durationDays: [5, [Validators.required, Validators.min(2), Validators.max(30)]],
      travelers: [1, [Validators.required, Validators.min(1), Validators.max(9)]],
      budgetAmount: [1000, [Validators.required, Validators.min(1), Validators.max(1_000_000)]],
      budgetCurrency: ['EUR', Validators.required],
      destinationMode: ['Automatic' as DestinationMode],
      destinationIds: [[] as string[]],
      destinationCityCodes: [[] as string[]],
      continents: [[] as Continent[]],
      preferences: this.fb.nonNullable.group({
        includeCheckedBaggage: [false],
        minAccommodationStars: [null as number | null, [Validators.min(1), Validators.max(5)]],
        maxFlightDurationHours: [null as number | null, [Validators.min(1), Validators.max(48)]],
        maxStops: [null as number | null, [Validators.min(0), Validators.max(3)]],
        preferredDepartureTime: ['Any' as DepartureTimePreference],
        includeDailyExpenseEstimates: [false],
        allowSelfTransfer: [true],
      }),
    },
    { validators: [tripDatesValidator(this.todayIso, this.maxWindow), manualDestinationsValidator(this.maxDestinations)] },
  );

  protected readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly isManual = computed(() => this.formValue().destinationMode === 'Manual');
  protected readonly nights = computed(() => Math.max(0, (this.formValue().durationDays ?? 0) - 1));
  protected readonly selectedIds = computed(() => this.formValue().destinationIds ?? []);
  protected readonly selectedContinents = computed(() => this.formValue().continents ?? []);
  protected readonly selectedCount = computed(() => this.selectedIds().length + (this.formValue().destinationCityCodes ?? []).length);
  /** World matches that are not catalog cities already listed or picked. */
  protected readonly worldOptions = computed(() => {
    const picked = new Set(this.formValue().destinationCityCodes ?? []);
    return this.worldMatches().filter((p) => !p.destinationId && !picked.has(p.cityCode));
  });
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
    return this.origin()?.city ?? this.airports().find((a) => a.iataCode === iata)?.city ?? iata;
  });
  protected readonly originLabel = (a: AirportDto) => `${a.city} (${a.iataCode})`;
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
    // Departure airport: the remembered one, else the one nearest to the visitor (by IP), else Tbilisi.
    if (!this.origin()) {
      this.api
        .getNearestAirport()
        .pipe(
          catchError(() => of(null)),
          switchMap((a) => (a ? of({ airport: a, suggested: true }) : this.api.searchAirports('TBS').pipe(
            catchError(() => of([])),
            switchMap((list) => of({ airport: list[0] ?? null, suggested: false })),
          ))),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe(({ airport, suggested }) => {
          if (airport && !this.origin()) {
            this.setOrigin(airport, false);
            this.originSuggested.set(suggested);
          }
        });
    }
    this.originQuery.set(this.origin() ? this.originLabel(this.origin()!) : '');
    this.originTerms
      .pipe(
        debounceTime(200),
        distinctUntilChanged(),
        switchMap((term) => (term.trim().length < 2 ? of([]) : this.api.searchAirports(term.trim()).pipe(catchError(() => of([]))))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((list) => {
        this.originOptions.set(list);
        this.originActive.set(list.length ? 0 : -1);
      });
    this.placeTerms
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((term) => (term.trim().length < 2 ? of([]) : this.api.searchPlaces(term.trim()).pipe(catchError(() => of([]))))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((list) => this.worldMatches.set(list));

    // Server-side field errors become stale as soon as the user edits the form.
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.serverFieldErrors.set({}));
  }

  protected setMode(mode: DestinationMode): void {
    this.form.controls.destinationMode.setValue(mode);
  }

  protected onOriginInput(value: string): void {
    this.originQuery.set(value);
    this.originOpen.set(true);
    this.originTerms.next(value);
  }

  protected onOriginKey(event: KeyboardEvent): void {
    const options = this.originOptions();
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      if (!options.length) return;
      this.originOpen.set(true);
      const step = event.key === 'ArrowDown' ? 1 : -1;
      this.originActive.set((this.originActive() + step + options.length) % options.length);
    } else if (event.key === 'Enter' && this.originOpen() && options[this.originActive()]) {
      event.preventDefault();
      this.setOrigin(options[this.originActive()]);
    } else if (event.key === 'Escape') {
      this.closeOrigin();
    }
  }

  /** Restores the selected airport's label when the field loses focus without a choice. */
  protected closeOrigin(): void {
    this.originOpen.set(false);
    const selected = this.origin();
    if (selected) this.originQuery.set(this.originLabel(selected));
  }

  /** Other airports in the departure airport's country, one tap away (Georgia: Kutaisi, Batumi). */
  protected readonly sameCountry = toSignal(
    toObservable(this.origin).pipe(
      map((o) => (o ? { code: o.countryCode, iata: o.iataCode, term: o.country ?? o.countryCode } : null)),
      distinctUntilChanged((a, b) => a?.iata === b?.iata),
      switchMap((o) =>
        !o ? of([] as AirportDto[]) : this.api.searchAirports(o.term).pipe(
          map((list) => list.filter((a) => a.countryCode === o.code && a.iataCode !== o.iata).slice(0, 6)),
          catchError(() => of([] as AirportDto[])),
        ),
      ),
    ),
    { initialValue: [] as AirportDto[] },
  );

  protected focusOrigin(input: HTMLInputElement): void {
    input.focus();
    input.select();
    this.originOpen.set(true);
  }

  protected setOrigin(airport: AirportDto, remember = true): void {
    this.origin.set(airport);
    this.originSuggested.set(false);
    this.form.controls.originAirportIata.setValue(airport.iataCode);
    this.form.controls.originAirportIata.markAsTouched();
    this.originQuery.set(this.originLabel(airport));
    this.originOpen.set(false);
    this.originOptions.set([]);
    if (remember) rememberOrigin(airport);
  }

  protected continentKey(c: Continent): TranslationKey {
    return `continent.${c}`;
  }

  protected toggleContinent(c: Continent): void {
    const current = this.form.controls.continents.value;
    this.form.controls.continents.setValue(current.includes(c) ? current.filter((x) => x !== c) : [...current, c]);
  }

  protected onDestinationFilter(value: string): void {
    this.destinationFilter.set(value);
    this.placeTerms.next(value);
  }

  protected addPlace(place: PlaceDto): void {
    if (place.destinationId) {
      this.addDestination(place.destinationId);
      return;
    }
    const codes = this.form.controls.destinationCityCodes.value;
    if (codes.includes(place.cityCode) || this.selectedCount() >= this.maxManual()) return;
    this.pickedPlaces.set([...this.pickedPlaces().filter((p) => p.cityCode !== place.cityCode), place]);
    this.form.controls.destinationCityCodes.setValue([...codes, place.cityCode]);
    this.destinationFilter.set('');
    this.worldMatches.set([]);
  }

  protected removePlace(cityCode: string): void {
    this.form.controls.destinationCityCodes.setValue(this.form.controls.destinationCityCodes.value.filter((c) => c !== cityCode));
    this.pickedPlaces.set(this.pickedPlaces().filter((p) => p.cityCode !== cityCode));
  }

  protected addDestination(id: string): void {
    const ids = this.form.controls.destinationIds.value;
    if (ids.includes(id) || this.selectedCount() >= this.maxManual()) return;
    this.form.controls.destinationIds.setValue([...ids, id]);
    this.destinationFilter.set('');
    this.worldMatches.set([]);
  }

  protected removeDestination(id: string): void {
    this.form.controls.destinationIds.setValue(this.form.controls.destinationIds.value.filter((x) => x !== id));
  }

  /** Destination cards preselect that city (manual mode) and bring the search panel into view. */
  protected pickDestination(d: DestinationSummaryDto): void {
    this.form.controls.destinationMode.setValue('Manual');
    this.form.controls.destinationIds.setValue([d.id]);
    this.form.controls.destinationCityCodes.setValue([]);
    this.pickedPlaces.set([]);
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
      destinationCityCodes: v.destinationMode === 'Manual' ? v.destinationCityCodes : null,
      continents: v.destinationMode === 'Automatic' ? v.continents : null,
      preferences: {
        includeCheckedBaggage: v.preferences.includeCheckedBaggage,
        minAccommodationStars: numberOrNull(v.preferences.minAccommodationStars),
        maxFlightDurationHours: numberOrNull(v.preferences.maxFlightDurationHours),
        maxStops: numberOrNull(v.preferences.maxStops),
        preferredDepartureTime: v.preferences.preferredDepartureTime,
        includeDailyExpenseEstimates: v.preferences.includeDailyExpenseEstimates,
        allowSelfTransfer: v.preferences.allowSelfTransfer,
      },
    };

    this.submitting.set(true);
    this.api.createTripSearch(command).subscribe({
      next: (res) => {
        this.recent.add(
          res.id,
          command,
          v.destinationMode === 'Manual' ? [...this.selectedDestinations().map((d) => d.city), ...this.pickedPlaces().map((p) => p.city)] : [],
        );
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
