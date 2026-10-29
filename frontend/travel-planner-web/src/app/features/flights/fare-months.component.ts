import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { toIsoDate } from '../../core/format';
import { FlightCalendarDayDto } from '../../core/models';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe } from '../../shared/pipes';

const MONTHS_AHEAD = 12;
const SHOWN = 3;

export interface DayPick {
  date: string;
  fare: FlightCalendarDayDto | null;
}

interface Cell {
  day: number;
  iso: string;
  fare: FlightCalendarDayDto | null;
  disabled: boolean;
}

/**
 * Month calendars of a route's cheapest known fare per day, three months at a time. Every future day can be picked
 * (a day without a known fare may still have flights); cheap days, the picked day and the trip range are highlighted.
 */
@Component({
  selector: 'app-fare-months',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, LocalDatePipe],
  styles: `
    :host { display: block; }
    .fm-nav { display: flex; align-items: center; justify-content: space-between; gap: 0.5rem; margin-bottom: 0.5rem; }
    .fm-nav-btn { display: inline-grid; place-items: center; width: 2.1rem; height: 2.1rem; border-radius: 999px; border: 1px solid var(--border);
      background: var(--surface); color: inherit; cursor: pointer; }
    .fm-nav-btn:disabled { opacity: 0.35; cursor: default; }
    .fm-nav-btn:not(:disabled):hover { border-color: var(--border-strong); }
    .fm-flip { transform: rotate(180deg); }
    .fm-months { display: grid; gap: 1.25rem; grid-template-columns: repeat(auto-fit, minmax(min(100%, 270px), 1fr)); }
    .fm-month { min-width: 0; }
    .fm-month h4 { font-size: 0.95rem; margin: 0 0 0.45rem; text-transform: capitalize; text-align: center; }
    .fm-grid { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 3px; }
    .fm-wd { font-size: 0.68rem; text-align: center; color: var(--muted); text-transform: uppercase; padding-bottom: 2px; }
    .fm-day { min-width: 0; min-height: 2.75rem; padding: 3px 1px; border-radius: 8px; border: 1px solid transparent; background: transparent;
      color: inherit; font: inherit; font-size: 0.78rem; display: flex; flex-direction: column; align-items: center; justify-content: center;
      gap: 1px; cursor: pointer; overflow: hidden; transition: background 0.12s, border-color 0.12s; }
    .fm-day:hover:not(:disabled) { border-color: var(--border-strong); }
    .fm-day:disabled { color: var(--muted); opacity: 0.4; cursor: default; }
    .fm-day.has-fare { background: var(--surface-tint); border-color: var(--border); }
    .fm-day.cheap { background: var(--success-soft); border-color: var(--success-border); }
    .fm-day.cheap .fm-price { color: var(--success); }
    .fm-day.in-range { background: color-mix(in srgb, var(--navy) 9%, transparent); }
    .fm-day.edge { background: var(--navy); border-color: var(--navy); color: #fff; }
    .fm-day.edge .fm-price { color: #fff; }
    .fm-num { font-weight: 600; line-height: 1.1; }
    .fm-price { font-size: 0.66rem; font-weight: 600; white-space: nowrap; letter-spacing: -0.02em; color: var(--text); }
    .fm-legend { display: flex; flex-wrap: wrap; gap: 0.35rem 1rem; margin-top: 0.75rem; font-size: 0.78rem; color: var(--muted); }
    .fm-key { display: inline-block; width: 0.8rem; height: 0.8rem; border-radius: 3px; vertical-align: -1px; margin-right: 0.3rem; border: 1px solid var(--border); }
    .fm-key.cheap { background: var(--success-soft); border-color: var(--success-border); }
    .fm-key.fare { background: var(--surface-tint); }
    .fm-key.edge { background: var(--navy); border-color: var(--navy); }
  `,
  template: `
    <div class="fm-nav">
      <button type="button" class="fm-nav-btn" (click)="page(-1)" [disabled]="start() === 0" [attr.aria-label]="'fcal.prevMonths' | t">
        <app-icon name="arrowRight" [size]="16" class="fm-flip" />
      </button>
      <span class="small muted">{{ months()[0].first | ldate: 'LLLL' }} – {{ months()[months().length - 1].first | ldate: 'LLLL y' }}</span>
      <button type="button" class="fm-nav-btn" (click)="page(1)" [disabled]="start() + SHOWN >= MONTHS_AHEAD" [attr.aria-label]="'fcal.nextMonths' | t">
        <app-icon name="arrowRight" [size]="16" />
      </button>
    </div>
    <div class="fm-months">
      @for (m of months(); track m.key) {
        <div class="fm-month">
          <h4>{{ m.first | ldate: 'LLLL y' }}</h4>
          <div class="fm-grid" role="grid">
            @for (w of weekdays(); track $index) { <span class="fm-wd" aria-hidden="true">{{ w }}</span> }
            @for (b of m.blanks; track $index) { <span></span> }
            @for (c of m.cells; track c.iso) {
              <button type="button" class="fm-day" [disabled]="c.disabled"
                      [class.has-fare]="!!c.fare" [class.cheap]="!!c.fare && c.fare.price <= cheapLimit()"
                      [class.edge]="c.iso === selected() || c.iso === rangeStart() || c.iso === rangeEnd()"
                      [class.in-range]="inRange(c.iso)"
                      [attr.aria-pressed]="c.iso === selected()"
                      [attr.aria-label]="(c.iso | ldate: 'EEEE d MMMM') + (c.fare ? ' · ' + price(c.fare.price) : '')"
                      (click)="picked.emit({ date: c.iso, fare: c.fare })">
                <span class="fm-num">{{ c.day }}</span>
                @if (c.fare; as f) { <span class="fm-price">{{ price(f.price) }}</span> }
              </button>
            }
          </div>
        </div>
      }
    </div>
    <p class="fm-legend">
      <span><span class="fm-key cheap"></span>{{ 'fcal.legendCheap' | t }}</span>
      <span><span class="fm-key fare"></span>{{ 'fcal.legendFare' | t }}</span>
      <span><span class="fm-key"></span>{{ 'fcal.legendUnknown' | t }}</span>
      <span><span class="fm-key edge"></span>{{ 'fcal.legendPicked' | t }}</span>
    </p>
  `,
})
export class FareMonthsComponent {
  readonly days = input.required<FlightCalendarDayDto[]>();
  readonly currency = input.required<string>();
  /** The picked day (ISO), and the trip it belongs to (departure to return), highlighted. */
  readonly selected = input<string | null>(null);
  readonly rangeStart = input<string | null>(null);
  readonly rangeEnd = input<string | null>(null);
  /** Days before this one cannot be picked (the return calendar starts at the departure day). */
  readonly minDate = input<string | null>(null);
  readonly picked = output<DayPick>();

  private readonly i18n = inject(I18nService);
  protected readonly SHOWN = SHOWN;
  protected readonly MONTHS_AHEAD = MONTHS_AHEAD;
  private readonly today = toIsoDate(new Date());
  private readonly firstMonth = new Date(new Date().getFullYear(), new Date().getMonth(), 1);
  /** First month shown; null until the visitor pages, so it follows the picked day or the first fare. */
  private readonly offset = signal<number | null>(null);

  protected readonly start = computed(() => {
    const own = this.offset();
    if (own !== null) return own;
    const anchor = this.selected() ?? this.minDate() ?? this.days()[0]?.date ?? this.today;
    const [y, m] = anchor.split('-').map(Number);
    const index = (y - this.firstMonth.getFullYear()) * 12 + (m - 1 - this.firstMonth.getMonth());
    return Math.min(Math.max(0, index), MONTHS_AHEAD - SHOWN);
  });

  /** Prices in the cheapest quarter of the route count as cheap. */
  protected readonly cheapLimit = computed(() => {
    const prices = this.days().map((d) => d.price).sort((a, b) => a - b);
    return prices.length ? prices[Math.floor((prices.length - 1) / 4)] : 0;
  });

  protected readonly weekdays = computed(() => {
    const format = new Intl.DateTimeFormat(this.i18n.intlLocale(), { weekday: 'short' });
    return [0, 1, 2, 3, 4, 5, 6].map((i) => format.format(new Date(2024, 0, 1 + i))); // 1 Jan 2024 was a Monday
  });

  protected readonly months = computed(() => {
    const byDate = new Map(this.days().map((d) => [d.date, d]));
    const min = [this.today, this.minDate() ?? ''].sort().at(-1)!;
    return Array.from({ length: SHOWN }, (_, i) => {
      const first = new Date(this.firstMonth.getFullYear(), this.firstMonth.getMonth() + this.start() + i, 1);
      const key = toIsoDate(first).slice(0, 7);
      const length = new Date(first.getFullYear(), first.getMonth() + 1, 0).getDate();
      const cells: Cell[] = Array.from({ length }, (_, d) => {
        const iso = `${key}-${String(d + 1).padStart(2, '0')}`;
        return { day: d + 1, iso, fare: byDate.get(iso) ?? null, disabled: iso < min };
      });
      return { key, first, blanks: Array((first.getDay() + 6) % 7).fill(0), cells };
    });
  });

  protected page(step: number): void {
    this.offset.set(Math.min(Math.max(0, this.start() + step * SHOWN), MONTHS_AHEAD - SHOWN));
  }

  protected inRange(iso: string): boolean {
    const a = this.rangeStart();
    const b = this.rangeEnd();
    return !!a && !!b && iso > a && iso < b;
  }

  /** Whole units ("€384"): cents do not fit a calendar cell. */
  protected price(value: number): string {
    return new Intl.NumberFormat(this.i18n.intlLocale(), { style: 'currency', currency: this.currency(), maximumFractionDigits: 0 }).format(value);
  }
}
