import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { IconComponent } from '../../shared/icon.component';
import { MoneyPipe } from '../../shared/pipes';

/** The nightly rate a stay budget allows: the whole-stay amount spread over the nights and rooms. */
export function nightlyAllowance(budget: number, perNight: boolean, nights: number, rooms: number): number {
  return perNight ? budget : budget / Math.max(1, nights) / Math.max(1, rooms);
}

/**
 * Booking.com search limited to a nightly price (its own "price" filter: nflt=price=EUR-min-50-1). Only for plain
 * booking.com search links; an affiliate deep link is returned unchanged.
 */
export function bookingWithMaxNightly(url: string, currency: string, maxNightly: number): string {
  try {
    const u = new URL(url);
    if (!/(^|\.)booking\.com$/i.test(u.hostname)) return url;
    u.searchParams.set('selected_currency', currency);
    u.searchParams.set('nflt', `price=${currency}-min-${Math.max(1, Math.floor(maxNightly))}-1`);
    return u.toString();
  } catch {
    return url;
  }
}

/**
 * "How much can you pay for the stay?": the traveller types an amount (for the whole stay or a night) and the hotel list
 * keeps only the hotels that fit; the same limit opens Booking.com's own price filter for more options.
 */
@Component({
  selector: 'app-stay-budget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, MoneyPipe],
  styles: `
    :host { display: block; }
    .sb { display: grid; gap: 0.6rem; padding: 0.9rem 1rem; border-radius: var(--radius-md); border: 1px solid var(--border);
      background: linear-gradient(135deg, var(--surface), var(--primary-soft)); }
    .sb-head { display: flex; gap: 0.5rem; align-items: center; font-weight: 700; font-size: 0.92rem; }
    .sb-row { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; }
    .sb-input { display: inline-flex; align-items: center; gap: 0.35rem; padding: 0 0.7rem; min-height: 2.6rem; border-radius: var(--radius-md);
      border: 1.5px solid var(--border-strong); background: var(--surface); }
    .sb-input:focus-within { border-color: var(--primary); box-shadow: 0 0 0 3px var(--primary-ring); }
    .sb-input input { width: 7rem; border: 0; outline: none; background: transparent; font: inherit; font-size: 1.05rem; font-weight: 700; color: var(--text); }
    .sb-cur { font-weight: 700; color: var(--muted); }
    .sb-mode { display: inline-flex; border: 1px solid var(--border); border-radius: 999px; overflow: hidden; background: var(--surface); }
    .sb-mode button { border: 0; background: none; padding: 0.4rem 0.8rem; font: inherit; font-size: 0.82rem; cursor: pointer; color: var(--text-2); }
    .sb-mode button[aria-pressed='true'] { background: var(--navy); color: #fff; }
    .sb-chip { border: 1px dashed var(--primary); background: none; color: var(--primary-deep); border-radius: 999px; padding: 0.3rem 0.75rem;
      font: inherit; font-size: 0.8rem; font-weight: 700; cursor: pointer; }
    .sb-clear { border: 0; background: none; color: var(--muted); font: inherit; font-size: 0.8rem; text-decoration: underline; cursor: pointer; }
    .sb-result { margin: 0; font-size: 0.88rem; }
    .sb-result strong { color: var(--primary-deep); }
    .sb-result.none strong { color: var(--danger, #b91c1c); }
    .sb-link { font-size: 0.82rem; font-weight: 700; }
  `,
  template: `
    <div class="sb" role="group" [attr.aria-label]="'budget.title' | t">
      <span class="sb-head"><app-icon name="wallet" [size]="18" /> {{ 'budget.title' | t }}</span>
      <div class="sb-row">
        <label class="sb-input">
          <span class="visually-hidden">{{ 'budget.amount' | t }}</span>
          <input type="number" inputmode="decimal" min="0" step="10" [value]="budget() ?? ''" [placeholder]="'budget.placeholder' | t"
                 (input)="onInput($any($event.target).value)" />
          <span class="sb-cur">{{ currency() }}</span>
        </label>
        <span class="sb-mode" role="group">
          <button type="button" [attr.aria-pressed]="!perNight()" (click)="perNight.set(false)">{{ 'budget.total' | t: { nights: nights() } }}</button>
          <button type="button" [attr.aria-pressed]="perNight()" (click)="perNight.set(true)">{{ 'budget.night' | t }}</button>
        </span>
        @if (remaining() > 0 && budget() === null) {
          <button type="button" class="sb-chip" (click)="perNight.set(false); budget.set(roundedRemaining())">
            {{ 'budget.useRemaining' | t: { amount: (roundedRemaining() | money: currency()) } }}
          </button>
        }
        @if (budget() !== null) { <button type="button" class="sb-clear" (click)="budget.set(null)">{{ 'budget.clear' | t }}</button> }
      </div>
      @if (budget() !== null && total() > 0) {
        <p class="sb-result" [class.none]="!fitCount()" aria-live="polite">
          @if (fitCount()) {
            <strong>{{ 'budget.fit' | t: { count: fitCount(), total: total() } }}</strong>
          } @else {
            <strong>{{ 'budget.noneFit' | t }}</strong> {{ 'budget.cheapest' | t: { amount: (cheapestStay() | money: currency()) } }}
          }
          · {{ 'budget.perNight' | t: { amount: (allowance() | money: currency()) } }}
        </p>
        @if (searchUrl(); as url) {
          <a class="sb-link" [href]="bookingUrl(url)" target="_blank" rel="noopener noreferrer nofollow">
            {{ 'budget.moreOnBooking' | t: { amount: (allowance() | money: currency()) } }} <app-icon name="external" [size]="13" />
            <span class="visually-hidden">{{ 'link.newTab' | t }}</span>
          </a>
        }
      } @else if (searchUrl(); as url) {
        <a class="sb-link" [href]="url" target="_blank" rel="noopener noreferrer nofollow">
          {{ 'budget.allOnBooking' | t }} <app-icon name="external" [size]="13" /><span class="visually-hidden">{{ 'link.newTab' | t }}</span>
        </a>
      }
    </div>
  `,
})
export class StayBudgetComponent {
  /** The amount the traveller can pay (null = no limit). */
  readonly budget = model<number | null>(null);
  /** True: the amount is per night; false: for the whole stay. */
  readonly perNight = model(false);
  readonly currency = input.required<string>();
  readonly nights = input.required<number>();
  readonly rooms = input(1);
  /** What is left of the trip budget, offered as a one-tap amount. */
  readonly remaining = input(0);
  readonly fitCount = input(0);
  readonly total = input(0);
  /** The whole stay at the cheapest listed hotel. */
  readonly cheapestStay = input(0);
  readonly searchUrl = input<string | undefined>();

  protected readonly allowance = computed(() => nightlyAllowance(this.budget() ?? 0, this.perNight(), this.nights(), this.rooms()));
  protected readonly roundedRemaining = computed(() => Math.floor(this.remaining() / 10) * 10);

  protected onInput(value: string): void {
    const n = Number(value);
    this.budget.set(value.trim() === '' || !Number.isFinite(n) || n <= 0 ? null : n);
  }

  protected bookingUrl(url: string): string {
    return bookingWithMaxNightly(url, this.currency(), this.allowance());
  }
}
