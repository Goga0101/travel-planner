import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ApiError } from '../core/errors';
import { TranslatePipe } from '../core/i18n/translate.pipe';

@Component({
  selector: 'app-error-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  template: `
    @if (error(); as e) {
      <div class="alert alert-error" role="alert">
        <strong>{{ e.titleKey ? (e.titleKey | t) : e.title }}</strong>
        @if (e.detailKey) { <span> {{ e.detailKey | t }}</span> } @else if (e.detail) { <span> {{ e.detail }}</span> }
        <ng-content />
      </div>
    }
  `,
})
export class ErrorBannerComponent {
  readonly error = input<ApiError | null>(null);
}
