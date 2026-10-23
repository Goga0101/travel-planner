import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { TranslationKey } from '../core/i18n/en';
import { I18nService } from '../core/i18n/i18n.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { BudgetFitStatus, VerificationStatus } from '../core/models';

type VerificationKey = VerificationStatus | 'Expired';

export const verificationLabelKey = (s: VerificationKey): TranslationKey => `verification.${s}.label`;
export const verificationDescKey = (s: VerificationKey): TranslationKey => `verification.${s}.desc`;

@Component({
  selector: 'app-verification-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="'badge badge-' + key().toLowerCase()" [title]="description()">{{ label() }}</span>`,
})
export class VerificationBadgeComponent {
  private readonly i18n = inject(I18nService);
  readonly status = input.required<VerificationStatus>();
  readonly expired = input(false);
  protected readonly key = computed<VerificationKey>(() => (this.expired() ? 'Expired' : this.status()));
  protected readonly label = computed(() => this.i18n.t(verificationLabelKey(this.key())));
  protected readonly description = computed(() => this.i18n.t(verificationDescKey(this.key())));
}

const FIT_CSS: Record<BudgetFitStatus, string> = {
  WithinBudget: 'fit-ok',
  WithinBudgetIncomplete: 'fit-partial',
  OverBudget: 'fit-over',
  Unknown: 'fit-unknown',
};

@Component({
  selector: 'app-budget-fit-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="fit-badge" [class]="'fit-badge ' + css()" [title]="description()">{{ label() }}</span>`,
})
export class BudgetFitBadgeComponent {
  private readonly i18n = inject(I18nService);
  readonly fit = input.required<BudgetFitStatus>();
  protected readonly css = computed(() => FIT_CSS[this.fit()]);
  protected readonly label = computed(() => this.i18n.t(`fit.${this.fit()}.label`));
  protected readonly description = computed(() => this.i18n.t(`fit.${this.fit()}.desc`));
}

@Component({
  selector: 'app-verification-legend',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [VerificationBadgeComponent, TranslatePipe],
  template: `
    <details class="legend">
      <summary>{{ 'legend.title' | t }}</summary>
      <dl>
        @for (s of statuses; track s) {
          <div class="legend-row">
            <dt><app-verification-badge [status]="s === 'Expired' ? 'Live' : s" [expired]="s === 'Expired'" /></dt>
            <dd>{{ descKey(s) | t }}</dd>
          </div>
        }
      </dl>
    </details>
  `,
})
export class VerificationLegendComponent {
  protected readonly statuses: VerificationKey[] = ['Live', 'Cached', 'Estimate', 'Verified', 'Demo', 'Unavailable', 'Expired'];
  protected readonly descKey = verificationDescKey;
}
