import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CategoryPipe } from '../../core/i18n/category.pipe';
import { TranslationKey, en } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { SourceVerificationDto, TripPlanSummaryDto } from '../../core/models';
import { BudgetFitBadgeComponent, VerificationBadgeComponent } from '../../shared/badges';
import { destinationPhoto, destinationSrcset } from '../../core/destination-photos';
import { SavedTripsService } from '../../core/local-lists';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe, MinutesPipe, MoneyPipe, StopsPipe } from '../../shared/pipes';

@Component({
  selector: 'app-plan-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LocalDatePipe, RouterLink, MoneyPipe, MinutesPipe, StopsPipe, TranslatePipe, CategoryPipe, BudgetFitBadgeComponent, VerificationBadgeComponent, IconComponent],
  template: `
    @let p = plan();
    <article class="card plan-card" [class.plan-over]="p.budgetFit === 'OverBudget'" [class.has-photo]="!!photo()">
      @if (photo(); as src) { <img class="plan-thumb" [src]="src" [attr.srcset]="srcset()" sizes="(min-width: 760px) 220px, 100vw" alt="" loading="lazy" decoding="async" width="400" height="300" /> }
      <div class="plan-card-main">
      <header class="plan-card-header">
        <div>
          <span class="rank" [attr.aria-label]="'card.rank' | t">#{{ p.rank }}</span>
          <h3>{{ p.destination.city }}<span class="muted">, {{ p.destination.country }}</span></h3>
          <p class="muted small">
            {{ p.departureDate | ldate: 'EEE d MMM' }} – {{ p.returnDate | ldate: 'EEE d MMM y' }} · {{ 'unit.nights' | t: { count: p.nights } }}
            · {{ 'unit.travelers' | t: { count: p.travelers } }}
          </p>
        </div>
        <div class="plan-price">
          <span class="price">{{ p.totalEstimatedCost | money: p.currency }}</span>
          <span class="small" [class.text-danger]="p.remainingBudget < 0">
            @if (p.remainingBudget >= 0) {
              {{ 'card.left' | t: { amount: (p.remainingBudget | money: p.currency) } }}
            } @else {
              {{ 'card.over' | t: { amount: ((-p.remainingBudget) | money: p.currency) } }}
            }
          </span>
          <app-budget-fit-badge [fit]="p.budgetFit" />
        </div>
      </header>

      @if (p.isDemo) {
        <p class="demo-tag">{{ 'card.demo' | t }}</p>
      }

      <div class="plan-sections">
        <section>
          <h4><app-icon name="plane" [size]="16" /> {{ 'card.flight' | t }}</h4>
          @if (p.flight; as f) {
            @if (f.outbound; as o) {
              <p>{{ o.from }} → {{ o.to }} · {{ o.departureLocal | ldate: 'd MMM HH:mm' }} · {{ o.durationMinutes | minutes }} · {{ o.stops | stops }}</p>
            }
            @if (f.inbound; as i) {
              <p>{{ i.from }} → {{ i.to }} · {{ i.departureLocal | ldate: 'd MMM HH:mm' }} · {{ i.durationMinutes | minutes }} · {{ i.stops | stops }}</p>
            }
            <p class="small">{{ f.price | money: f.currency }} · {{ f.provider }}
              @if (f.includedCheckedBags !== undefined) {
                · {{ f.includedCheckedBags > 0 ? ('unit.checkedBags' | t: { count: f.includedCheckedBags }) : ('card.noCheckedBag' | t) }}
              }
            </p>
          } @else {
            <p class="muted">{{ 'card.noFlight' | t }}</p>
          }
        </section>

        <section>
          <h4><app-icon name="bed" [size]="16" /> {{ 'card.stay' | t }}</h4>
          @if (p.accommodation; as a) {
            <p>{{ a.name }}</p>
            @if (a.verification === 'Unavailable') {
              <p class="small muted">{{ 'card.stayPriceMissing' | t }} @if (a.hasBookingLink) { {{ 'card.searchLinkInDetails' | t }} }</p>
            } @else {
              <p class="small">
                @if (a.stars) { {{ a.stars }}★ · }
                @if (a.reviewScore) { {{ a.reviewScore }}/10 · }
                {{ a.price | money: a.currency }}
              </p>
            }
          } @else {
            <p class="muted">{{ 'card.noStay' | t }}</p>
          }
        </section>

        <section>
          <h4><app-icon name="train" [size]="16" /> {{ 'card.transfer' | t }}</h4>
          @if (p.transfer; as tr) {
            <p>{{ tr.selectedOption ?? ('card.seeOptions' | t) }}</p>
            <p class="small">
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
        </section>
      </div>

      @if (p.missingCostCategories.length) {
        <p class="alert alert-warn small">{{ 'card.missingCosts' | t: { list: (p.missingCostCategories | category) } }}</p>
      }

      <footer class="plan-card-footer">
        <div class="sources" [attr.aria-label]="'card.sources' | t">
          @for (s of p.sources; track $index) {
            <span class="source">
              <span class="small muted">{{ sourceKey(s) | t }}</span>
              <app-verification-badge [status]="s.verification" [expired]="s.isExpired" />
            </span>
          }
        </div>
        <div class="card-actions">
        <button type="button" class="btn btn-sm save-toggle" [class.is-saved]="saved()" [attr.aria-pressed]="saved()" (click)="toggleSaved()"
                [attr.aria-label]="(saved() ? 'plan.saved' : 'plan.save') | t">
          <app-icon name="heart" [size]="16" /> {{ (saved() ? 'plan.saved' : 'plan.save') | t }}
        </button>
        <a class="btn btn-primary" [routerLink]="i18n.path('plans', p.id)">{{ 'card.viewDetails' | t }} <app-icon name="arrowRight" [size]="16" /></a>
        </div>
      </footer>
      <p class="small muted">{{ 'card.score' | t: { score: p.score } }} @if (scoreText()) { ({{ scoreText() }}) }</p>
      </div>
    </article>
  `,
})
export class PlanCardComponent {
  protected readonly i18n = inject(I18nService);
  readonly plan = input.required<TripPlanSummaryDto>();
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
