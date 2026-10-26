import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, combineLatest, of, switchMap, takeWhile, timer } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ApiError, toApiError } from '../../core/errors';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PlanSort, TripPlanSummaryDto, TripSearchDto } from '../../core/models';
import { VerificationLegendComponent } from '../../shared/badges';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe, MoneyPipe } from '../../shared/pipes';
import { PlanCardComponent } from './plan-card.component';

const POLL_MS = 1500;

@Component({
  selector: 'app-search-results-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LocalDatePipe, FormsModule, RouterLink, MoneyPipe, TranslatePipe, PlanCardComponent, ErrorBannerComponent, VerificationLegendComponent, IconComponent],
  templateUrl: './search-results.page.html',
})
export class SearchResultsPage {
  /** Route parameter (component input binding). */
  readonly id = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly i18n = inject(I18nService);

  protected readonly search = signal<TripSearchDto | null>(null);
  protected readonly loadError = signal<ApiError | null>(null);
  protected readonly transientError = signal(false);
  protected readonly plans = signal<TripPlanSummaryDto[]>([]);
  protected readonly plansLoading = signal(false);
  protected readonly plansError = signal<ApiError | null>(null);
  protected readonly cancelling = signal(false);

  protected readonly sort = signal<PlanSort>('Rank');
  protected readonly maxTotal = signal<number | null>(null);
  protected readonly hideIncomplete = signal(false);
  protected readonly destinationFilter = signal('');

  protected readonly sortOptions: { value: PlanSort; label: TranslationKey }[] = [
    { value: 'Rank', label: 'sort.Rank' },
    { value: 'TotalCost', label: 'sort.TotalCost' },
    { value: 'RemainingBudget', label: 'sort.RemainingBudget' },
    { value: 'DepartureDate', label: 'sort.DepartureDate' },
  ];

  protected readonly destinationsInResults = computed(() =>
    [...new Map(this.plans().map((p) => [p.destination.id, p.destination])).values()].sort((a, b) => a.city.localeCompare(b.city)),
  );

  protected readonly visiblePlans = computed(() => {
    const max = this.maxTotal();
    const dest = this.destinationFilter();
    return this.plans().filter(
      (p) =>
        (max === null || p.totalEstimatedCost <= max) &&
        (!this.hideIncomplete() || p.budgetFit === 'WithinBudget') &&
        (!dest || p.destination.id === dest),
    );
  });

  protected readonly destinationNames = computed(() => (this.search()?.criteria.destinations ?? []).map((d) => d.city).join(', '));

  protected readonly isNoMatch =computed(() => this.search()?.status === 'CompletedNoMatch');
  protected readonly hasCompleted = computed(() => {
    const s = this.search()?.status;
    return s === 'Completed' || s === 'CompletedNoMatch';
  });

  constructor() {
    // Poll the server for status until the search finishes. State always comes from the server, so a page reload resumes correctly.
    // Re-fetches when the language changes too, so server-generated texts follow the selected language.
    combineLatest([toObservable(this.id), toObservable(this.i18n.lang)])
      .pipe(
        switchMap(([id]) =>
          timer(0, POLL_MS).pipe(
            switchMap(() =>
              this.api.getTripSearch(id).pipe(
                catchError((e) => {
                  const error = toApiError(e);
                  if (error.status === 404 || error.status === 400) {
                    this.loadError.set(error);
                    return of('stop' as const);
                  }
                  this.transientError.set(true);
                  return of(null);
                }),
              ),
            ),
            takeWhile((s) => s !== 'stop' && !(s && s.isFinished), true),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((s) => {
        if (!s || s === 'stop') return;
        this.transientError.set(false);
        this.search.set(s);
        if (s.status === 'Completed' || s.status === 'CompletedNoMatch') this.loadPlans();
      });
  }

  protected changeSort(sort: PlanSort): void {
    this.sort.set(sort);
    this.loadPlans();
  }

  protected setMaxTotal(value: string): void {
    const n = Number(value);
    this.maxTotal.set(value === '' || !Number.isFinite(n) ? null : n);
  }

  protected cancel(): void {
    const s = this.search();
    if (!s || s.isFinished) return;
    this.cancelling.set(true);
    this.api.cancelTripSearch(s.id).subscribe({
      next: (updated) => {
        this.search.set(updated);
        this.cancelling.set(false);
      },
      error: (e) => {
        this.loadError.set(toApiError(e));
        this.cancelling.set(false);
      },
    });
  }

  protected loadPlans(): void {
    this.plansLoading.set(true);
    this.plansError.set(null);
    this.api
      .getPlans(this.id(), 1, 50, this.sort())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.plans.set(page.items);
          this.plansLoading.set(false);
        },
        error: (e) => {
          this.plansError.set(toApiError(e));
          this.plansLoading.set(false);
        },
      });
  }
}
