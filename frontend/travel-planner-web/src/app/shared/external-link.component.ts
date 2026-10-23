import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslatePipe } from '../core/i18n/translate.pipe';

/** Returns the URL only if it is an absolute https: URL without embedded credentials; otherwise null. */
export function safeHttpsUrl(value: string | null | undefined): string | null {
  if (!value) return null;
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:' || url.username || url.password) return null;
    return url.href;
  } catch {
    return null;
  }
}

/**
 * Renders an external link only for safe https URLs, always opening in a new tab with
 * rel="noopener noreferrer nofollow". Unsafe or missing URLs render the label as plain text.
 */
@Component({
  selector: 'app-external-link',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  template: `
    @if (safeUrl(); as url) {
      <a class="ext-link" [class.btn]="button()" [href]="url" target="_blank" rel="noopener noreferrer nofollow">
        {{ label() }}<span class="ext-icon" aria-hidden="true"> ↗</span><span class="visually-hidden"> {{ 'link.newTab' | t }}</span>
      </a>
    } @else {
      <span class="ext-link-disabled">{{ fallback() ?? label() }}</span>
    }
  `,
})
export class ExternalLinkComponent {
  readonly href = input<string | null | undefined>(null);
  readonly label = input.required<string>();
  /** Text shown instead of the label when the URL is missing/unsafe. */
  readonly fallback = input<string | null>(null);
  readonly button = input(false);
  protected readonly safeUrl = computed(() => safeHttpsUrl(this.href()));
}
