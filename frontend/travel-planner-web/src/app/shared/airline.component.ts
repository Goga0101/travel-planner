import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { AirlinesService } from '../core/airlines';
import { TranslatePipe } from '../core/i18n/translate.pipe';

/**
 * An airline shown by name ("Georgian Airways"); pointing at it (or tapping it on a phone) shows its code and name together.
 * With a label (a flight number such as "GQ785") the label is shown and the card names the airline.
 */
@Component({
  selector: 'app-airline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  styles: `
    :host { position: relative; display: inline-block; }
    .al { cursor: help; border-bottom: 1px dotted currentColor; outline: none; border-radius: 2px; }
    .al:focus-visible { box-shadow: 0 0 0 3px var(--primary-ring); }
    .al-tip { position: absolute; z-index: 30; left: 50%; bottom: calc(100% + 8px); transform: translate(-50%, 4px);
      width: max-content; max-width: 16rem; padding: 0.45rem 0.65rem; border-radius: var(--radius-sm, 8px);
      background: var(--navy); color: #fff; font-size: 0.8rem; font-weight: 500; line-height: 1.35; text-align: left; white-space: normal;
      box-shadow: 0 8px 24px rgba(15, 23, 42, 0.25); opacity: 0; visibility: hidden; pointer-events: none;
      transition: opacity 0.12s ease, transform 0.12s ease, visibility 0.12s; }
    .al-tip::after { content: ''; position: absolute; top: 100%; left: 50%; margin-left: -6px; border: 6px solid transparent; border-top-color: var(--navy); }
    .al-code { display: inline-block; margin-right: 0.35rem; padding: 0 0.35rem; border-radius: 4px; background: rgba(255, 255, 255, 0.16);
      font-weight: 700; letter-spacing: 0.04em; }
    .al-low { display: block; margin-top: 0.15rem; color: #fcd34d; font-size: 0.74rem; }
    :host(:hover) .al-tip, .al:focus + .al-tip { opacity: 1; visibility: visible; transform: translate(-50%, 0); }
  `,
  template: `
    @let a = airline();
    @if (a.name) {
      <span class="al" tabindex="0" [attr.aria-label]="a.code + ' · ' + a.name">{{ label() ?? a.name }}</span>
      <span class="al-tip" role="tooltip" aria-hidden="true">
        <span class="al-code">{{ a.code }}</span>{{ a.name }}
        @if (a.lowcost) { <span class="al-low">{{ 'airline.lowcost' | t }}</span> }
      </span>
    } @else {
      {{ label() ?? a.code }}
    }
  `,
})
export class AirlineComponent {
  /** IATA airline code ("A9") or a flight number ("GQ785"). */
  readonly code = input.required<string>();
  readonly label = input<string | null>(null);

  private readonly airlines = inject(AirlinesService);
  protected readonly airline = computed(() => this.airlines.get(AirlinesService.codeOf(this.code())));
}
