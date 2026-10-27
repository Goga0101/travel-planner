import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiService } from '../../core/api.service';
import { RouterLink } from '@angular/router';
import { CategoryPipe } from '../../core/i18n/category.pipe';
import { TranslationKey, en } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { HotelPricesDto, SourceVerificationDto, TripPlanSummaryDto } from '../../core/models';
import { BudgetFitBadgeComponent, VerificationBadgeComponent } from '../../shared/badges';
import { destinationPhoto, destinationSrcset } from '../../core/destination-photos';
import { SavedTripsService } from '../../core/local-lists';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe, MinutesPipe, MoneyPipe, StopsPipe } from '../../shared/pipes';
import { FlightRouteComponent, FlightTimelineComponent } from '../../shared/flight-route.component';

@Component({
  selector: 'app-plan-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LocalDatePipe, RouterLink, MoneyPipe, MinutesPipe, StopsPipe, TranslatePipe, CategoryPipe, BudgetFitBadgeComponent, VerificationBadgeComponent, IconComponent, FlightRouteComponent, FlightTimelineComponent],
  template: `
    @let p = plan();
    <article class="card plan-card" [class.plan-over]="p.budgetFit === 'OverBudget'" [class.plan-top]="p.rank === 1"
             [class.plan-self]="!!p.flight?.selfTransfer">
      <div class="plan-card-main">
        <header class="plan-card-header">
          <span class="rank" [attr.aria-label]="'card.rank' | t">#{{ p.rank }} @if (p.rank === 1) { · {{ 'card.topPick' | t }} }</span>
          <h3>{{ p.destination.city }}, {{ p.destination.country }}
            <span class="plan-dates-inline">({{ p.departureDate | ldate: 'd MMM' }} – {{ p.returnDate | ldate: 'd MMM y' }})</span>
          </h3>
          <span class="plan-tags">
            <span class="meta-chip"><app-icon name="bed" [size]="14" /> {{ 'unit.nights' | t: { count: p.nights } }}</span>
            <span class="meta-chip"><app-icon name="users" [size]="14" /> {{ 'unit.travelers' | t: { count: p.travelers } }}</span>
            @if (p.flight?.selfTransfer) { <span class="badge-self">{{ 'flight.selfTransferBadge' | t }}</span> }
          </span>
        </header>

        @if (p.isDemo) {
          <p class="demo-tag">{{ 'card.demo' | t }}</p>
        }

        <section class="plan-flight" [attr.aria-label]="'card.flight' | t">
          @if (p.flight; as f) {
            <div class="plan-flight-head">
              <span class="block-title"><app-icon name="plane" [size]="16" /> {{ 'card.flight' | t }}</span>
              <span class="small muted">{{ f.price | money: f.currency }} · {{ f.provider }}
                @if (f.includedCheckedBags !== undefined) {
                  · {{ f.includedCheckedBags > 0 ? ('unit.checkedBags' | t: { count: f.includedCheckedBags }) : ('card.noCheckedBag' | t) }}
                }
              </span>
            </div>
            @if (f.outbound; as o) { <app-flight-timeline [leg]="o" [selfTransfer]="f.selfTransfer" /> }
            @if (f.inbound; as i) {
              <p class="plan-return small">
                <strong>{{ 'plan.return' | t }}:</strong> <app-flight-route [leg]="i" /> · {{ i.departureLocal | ldate: 'd MMM HH:mm' }}
                · {{ i.durationMinutes | minutes }} · {{ i.stops | stops }}
              </p>
            }
          } @else {
            <p class="muted">{{ 'card.noFlight' | t }}</p>
          }
        </section>

        <div class="plan-tiles">
          <section class="plan-tile">
            <app-icon name="bed" [size]="18" class="plan-tile-icon" />
            <div>
              <span class="block-title">{{ 'card.stay' | t }}</span>
              @if (p.accommodation; as a) {
                @if (a.verification === 'Unavailable') {
                  @if (prices(); as hp) {
                    @if (hp.bookingComNightlyMin !== undefined && hp.bookingComNightlyMin !== null) {
                      <p class="price-hint"><strong>Booking.com: {{ hp.bookingComNightlyMin | money: hp.currency }} – {{ hp.bookingComNightlyMax | money: hp.currency }}</strong> {{ 'card.perNight' | t }}</p>
                    } @else {
                      <p class="price-hint"><strong>{{ 'card.fromPerNight' | t: { amount: (hp.lowestNightly | money: hp.currency) } }}</strong></p>
                    }
                    <p class="small">{{ 'card.stayEstimate' | t: { amount: (hp.estimatedStayTypical | money: hp.currency), nights: ('unit.nights' | t: { count: hp.nights }) } }}</p>
                    <p class="small muted">{{ 'card.estimateNote' | t }}</p>
                  } @else if (pricesLoading()) {
                    <p class="small muted"><span class="spinner" aria-hidden="true"></span> {{ 'card.pricesLoading' | t }}</p>
                  } @else {
                    <p class="small muted">{{ 'card.stayPriceMissing' | t }} @if (a.hasBookingLink) { {{ 'card.searchLinkInDetails' | t }} }</p>
                  }
                } @else {
                  <p>{{ a.name }}</p>
                  <p class="small">
                    @if (a.stars) { {{ a.stars }}★ · }
                    @if (a.reviewScore) { {{ a.reviewScore }}/10 · }
                    {{ a.price | money: a.currency }}
                  </p>
                }
              } @else {
                <p class="muted">{{ 'card.noStay' | t }}</p>
              }
            </div>
          </section>

          <section class="plan-tile">
            <app-icon name="train" [size]="18" class="plan-tile-icon" />
            <div>
              <span class="block-title">{{ 'card.transfer' | t }}</span>
              @if (p.transfer; as tr) {
                <p>{{ tr.selectedOption ?? ('card.seeOptions' | t) }}</p>
                <p class="small muted">
                  @if (tr.durationMinutes) { ~{{ tr.durationMinutes | minutes }} · }
                  @if (tr.estimatedCost !== undefined) {
                    {{ 'card.estEachWay' | t: { amount: (tr.estimatedCost | money: tr.currency) } }}
                  } @else {
                    {{ 'card.costUnavailable' | t }}
                  }
                </p>
              } @else {
                <p class="muted">{{ 'card.noTransfer' | t }}</p>
              }
            </div>
          </section>
        </div>

        @if (p.missingCostCategories.length) {
          <p class="alert alert-warn small">{{ 'card.missingCosts' | t: { list: (p.missingCostCategories | category) } }}</p>
        }

        <div class="sources" [attr.aria-label]="'card.sources' | t">
          @for (s of p.sources; track $index) {
            <span class="source">
              <span class="small muted">{{ sourceKey(s) | t }}</span>
              <app-verification-badge [status]="s.verification" [expired]="s.isExpired" />
            </span>
          }
        </div>
      </div>

      <aside class="plan-side">
        <span class="side-label">{{ 'card.total' | t }}</span>
        <span class="price price-lg">{{ p.totalEstimatedCost | money: p.currency }}</span>
        <span class="budget-delta" [class.over]="p.remainingBudget < 0">
          @if (p.remainingBudget >= 0) {
            {{ 'card.left' | t: { amount: (p.remainingBudget | money: p.currency) } }}
          } @else {
            {{ 'card.over' | t: { amount: ((-p.remainingBudget) | money: p.currency) } }}
          }
        </span>
        <app-budget-fit-badge [fit]="p.budgetFit" />
        @if (prices(); as hp) {
          <span class="small trip-estimate" [title]="'card.tripEstimateHint' | t">
            {{ 'card.tripEstimate' | t: { amount: (hp.estimatedTripTotal | money: hp.currency) } }}
          </span>
        }
        <div class="plan-side-actions">
          <a class="btn btn-dark" [routerLink]="i18n.path('plans', p.id)">{{ 'card.viewDetails' | t }} <app-icon name="arrowRight" [size]="16" /></a>
          <button type="button" class="btn btn-sm btn-ghost save-toggle" [class.is-saved]="saved()" [attr.aria-pressed]="saved()" (click)="toggleSaved()"
                  [attr.aria-label]="(saved() ? 'plan.saved' : 'plan.save') | t">
            <app-icon name="heart" [size]="16" /> {{ (saved() ? 'plan.saved' : 'plan.save') | t }}
          </button>
        </div>
        <p class="small muted plan-score" [title]="scoreText()">{{ 'card.score' | t: { score: p.score } }}</p>
      </aside>
    </article>
  `,
})
export class PlanCardComponent {
  protected readonly i18n = inject(I18nService);
  readonly plan = input.required<TripPlanSummaryDto>();
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  /** Indicative hotel prices for this plan's dates (loaded after the card renders; never part of the total). */
  protected readonly prices = signal<HotelPricesDto | null>(null);
  protected readonly pricesLoading = signal(false);

  constructor() {
    effect(() => {
      const p = this.plan();
      untracked(() => {
        // Only plans without a priced stay need the estimate.
        if (p.accommodation && p.accommodation.verification !== 'Unavailable') return;
        this.pricesLoading.set(true);
        this.api
          .getHotelPrices(p.id)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: (hp) => {
              this.prices.set(hp);
              this.pricesLoading.set(false);
            },
            error: () => this.pricesLoading.set(false),
          });
      });
    });
  }
  protected readonly photo = computed(() => destinationPhoto(this.plan().destination.code));
  protected readonly srcset = computed(() => destinationSrcset(this.plan().destination.code));
  private readonly savedTrips = inject(SavedTripsService);
  protected readonly saved = computed(() => {
    this.savedTrips.trips();
    return this.savedTrips.isSaved(this.plan().id);
  });

  protected toggleSaved(): void {
    this.savedTrips.toggle(this.plan());
  }

  protected readonly scoreText = computed(() =>
    Object.entries(this.plan().scoreBreakdown)
      .map(([k, v]) => {
        const key = `score.${k}`;
        return `${key in en ? this.i18n.t(key as TranslationKey) : k} ${v}`;
      })
      .join(', '),
  );

  protected sourceKey(s: SourceVerificationDto): TranslationKey {
    if (s.type !== 'Transfer') return `itemType.${s.type}`;
    return s.direction === 'AirportToAccommodation' ? 'card.transferIn' : 'card.transferOut';
  }
}
