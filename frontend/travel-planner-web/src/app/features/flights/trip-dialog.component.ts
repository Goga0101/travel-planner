import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, computed, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ApiError, toApiError } from '../../core/errors';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { FlightCalendarDayDto, FlightOptionsDto } from '../../core/models';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { ExternalLinkComponent } from '../../shared/external-link.component';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe, StopsPipe } from '../../shared/pipes';
import { FareMonthsComponent } from './fare-months.component';
import { FlightOptionsComponent } from './flight-options.component';

/** The day picked on a route's calendar: the route and the cheapest fare of that day. */
export interface PickedFlight {
  fromCode: string;
  toCode: string;
  fromName: string;
  toName: string;
  currency: string;
  fare: FlightCalendarDayDto;
}

type Mode = 'ask' | 'round' | 'oneway';

/**
 * Opens on a calendar day: the flight of that day, then "do you also want a ticket back?" — the return calendar with the
 * cheapest days back, and the flights for the chosen dates with their prices and booking links; or the one-way flights.
 */
@Component({
  selector: 'app-trip-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, ExternalLinkComponent, ErrorBannerComponent, LocalDatePipe, StopsPipe, FareMonthsComponent,
    FlightOptionsComponent],
  styles: `
    dialog { width: min(1000px, calc(100vw - 32px)); max-height: calc(100dvh - 32px); padding: 0; border: 0; border-radius: var(--radius-lg);
      background: var(--bg); color: var(--text); box-shadow: 0 30px 80px rgba(8, 24, 40, .35); overflow: hidden; }
    dialog[open] { display: flex; flex-direction: column; animation: td-in 0.18s ease-out; }
    dialog::backdrop { background: rgba(10, 24, 38, .55); backdrop-filter: blur(3px); }
    @keyframes td-in { from { opacity: 0; transform: translateY(12px) scale(.985); } }
    .td-head { display: flex; align-items: flex-start; gap: 1rem; padding: 1.1rem 1.35rem; background: var(--navy); color: #fff; }
    .td-head h2 { margin: 0; font-size: 1.2rem; color: #fff; }
    .td-head p { margin: 0.15rem 0 0; opacity: 0.8; font-size: 0.88rem; text-transform: capitalize; }
    .td-close { margin-left: auto; flex: none; width: 2.2rem; height: 2.2rem; border-radius: 999px; border: 1px solid rgba(255,255,255,.3);
      background: transparent; color: #fff; cursor: pointer; display: grid; place-items: center; }
    .td-close:hover { background: rgba(255,255,255,.12); }
    .td-body { overflow: auto; padding: 1.25rem 1.35rem 1.5rem; display: grid; gap: 1.25rem; }
    .td-flight { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 1rem; align-items: center; padding: 1rem 1.15rem;
      border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--surface); }
    .td-flight-icon { width: 2.6rem; height: 2.6rem; border-radius: 999px; display: grid; place-items: center; background: var(--primary-soft); color: var(--primary-deep); }
    .td-flight-main strong { font-size: 1.05rem; }
    .td-flight-side { text-align: right; display: grid; gap: 0.3rem; justify-items: end; }
    .td-price { font-size: 1.45rem; font-weight: 800; line-height: 1; }
    .td-ask { text-align: center; padding: 0.5rem 0 0.25rem; }
    .td-ask h3 { margin: 0 0 0.85rem; font-size: 1.2rem; }
    .td-choices { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.75rem; max-width: 640px; margin: 0 auto; }
    .td-choice { border: 1.5px solid var(--border); border-radius: var(--radius-md); background: var(--surface); padding: 1rem; cursor: pointer;
      font: inherit; color: inherit; display: grid; gap: 0.3rem; justify-items: center; text-align: center; transition: border-color .15s, box-shadow .15s, transform .15s; }
    .td-choice:hover { border-color: var(--primary); transform: translateY(-1px); box-shadow: var(--shadow); }
    .td-choice.active { border-color: var(--primary); box-shadow: 0 0 0 3px var(--primary-ring); }
    .td-choice-icon { width: 2.4rem; height: 2.4rem; border-radius: 999px; display: grid; place-items: center; background: var(--primary-soft); color: var(--primary-deep); }
    .td-choice strong { font-size: 1rem; }
    .td-tabs { display: flex; justify-content: center; }
    .td-section-title { display: flex; align-items: baseline; justify-content: space-between; gap: 0.75rem; flex-wrap: wrap; margin: 0 0 0.6rem; }
    .td-section-title h3 { margin: 0; font-size: 1.05rem; }
    .td-best { display: flex; gap: 0.5rem; overflow-x: auto; padding: 0.15rem 0.1rem 0.6rem; scrollbar-width: thin; }
    .td-best button { flex: none; border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--surface); padding: 0.55rem 0.8rem;
      cursor: pointer; font: inherit; color: inherit; display: grid; gap: 0.1rem; text-align: left; min-width: 8.5rem; transition: border-color .12s; }
    .td-best button:hover { border-color: var(--primary); }
    .td-best button.active { border-color: var(--navy); background: var(--navy); color: #fff; }
    .td-best .d { font-size: 0.8rem; text-transform: capitalize; }
    .td-best .p { font-weight: 800; font-size: 1.05rem; }
    .td-best .n { font-size: 0.72rem; opacity: 0.75; }
    .td-card { border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--surface); padding: 1rem; }
    .td-total { display: flex; flex-wrap: wrap; gap: 0.5rem 1rem; align-items: center; justify-content: space-between; padding: 0.8rem 1rem;
      border-radius: var(--radius-md); background: var(--primary-soft); color: var(--primary-deep); }
    .td-total strong { font-size: 1.1rem; }
    .td-pax { display: inline-flex; align-items: center; gap: 0.4rem; }
    .td-pax button { width: 1.8rem; height: 1.8rem; border-radius: 999px; border: 1px solid var(--border); background: var(--surface); cursor: pointer; font-weight: 700; }
    .td-skeleton { height: 9rem; border-radius: var(--radius-md); background: linear-gradient(90deg, var(--surface-tint), var(--surface), var(--surface-tint));
      background-size: 200% 100%; animation: td-sh 1.2s linear infinite; }
    @keyframes td-sh { to { background-position: -200% 0; } }
    @media (max-width: 640px) {
      .td-flight { grid-template-columns: minmax(0, 1fr); }
      .td-flight-icon { display: none; }
      .td-flight-side { justify-items: start; text-align: left; }
      .td-choices { grid-template-columns: 1fr; }
    }
  `,
  template: `
    <dialog #dialog (close)="closed.emit()" (click)="backdropClick($event)" [attr.aria-labelledby]="'td-title'">
      @if (flight(); as f) {
        <header class="td-head">
          <div>
            <h2 id="td-title">{{ f.fromName }} → {{ f.toName }}</h2>
            <p>{{ f.fare.date | ldate: 'EEEE, d MMMM y' }}</p>
          </div>
          <button type="button" class="td-close" (click)="close()" [attr.aria-label]="'fcal.close' | t"><app-icon name="x" [size]="18" /></button>
        </header>

        <div class="td-body">
          <div class="td-flight">
            <span class="td-flight-icon"><app-icon name="plane" [size]="20" /></span>
            <div class="td-flight-main">
              <strong>{{ f.fare.time }} · {{ f.fare.from }} → {{ f.fare.to }}</strong>
              <div class="small muted">{{ f.fare.stops | stops }}@if (f.fare.airline) { · {{ f.fare.airline }} } · {{ 'fcal.oneWayPerPerson' | t }}</div>
            </div>
            <div class="td-flight-side">
              <span class="td-price">{{ money(f.fare.price) }}</span>
              <app-external-link [href]="f.fare.bookingUrl" [label]="'fcal.bookThis' | t" />
            </div>
          </div>

          @if (mode() === 'ask') {
            <div class="td-ask">
              <h3>{{ 'fcal.askReturn' | t }}</h3>
              <div class="td-choices">
                <button type="button" class="td-choice" (click)="setMode('round')">
                  <span class="td-choice-icon"><app-icon name="shuffle" [size]="18" /></span>
                  <strong>{{ 'fcal.yesReturn' | t }}</strong>
                  <span class="small muted">{{ 'fcal.yesReturnHint' | t }}</span>
                </button>
                <button type="button" class="td-choice" (click)="setMode('oneway')">
                  <span class="td-choice-icon"><app-icon name="arrowRight" [size]="18" /></span>
                  <strong>{{ 'fcal.noReturn' | t }}</strong>
                  <span class="small muted">{{ 'fcal.noReturnHint' | t }}</span>
                </button>
              </div>
            </div>
          } @else {
            <div class="td-tabs">
              <div class="segmented" role="radiogroup">
                <button type="button" role="radio" [class.active]="mode() === 'round'" [attr.aria-checked]="mode() === 'round'" (click)="setMode('round')">
                  <app-icon name="shuffle" [size]="14" /> {{ 'fcal.roundTrip' | t }}
                </button>
                <button type="button" role="radio" [class.active]="mode() === 'oneway'" [attr.aria-checked]="mode() === 'oneway'" (click)="setMode('oneway')">
                  <app-icon name="arrowRight" [size]="14" /> {{ 'fcal.oneWay' | t }}
                </button>
              </div>
            </div>
          }

          @if (mode() === 'round') {
            <section>
              <div class="td-section-title">
                <h3>{{ 'fcal.whenBack' | t: { route: f.toName + ' → ' + f.fromName } }}</h3>
                @if (!returnLoading() && returnDays().length) { <span class="small muted">{{ 'fcal.whenBackHint' | t }}</span> }
              </div>
              @if (returnLoading()) {
                <div class="td-skeleton"></div>
              } @else {
                @if (bestReturns().length) {
                  <div class="td-best" role="list">
                    @for (d of bestReturns(); track d.date) {
                      <button type="button" role="listitem" [class.active]="d.date === returnDate()" (click)="pickReturn(d.date)">
                        <span class="d">{{ d.date | ldate: 'EEE, d MMM' }}</span>
                        <span class="p">{{ money(d.price) }}</span>
                        <span class="n">{{ 'unit.days' | t: { count: tripDays(d.date) } }} · {{ d.stops | stops }}</span>
                      </button>
                    }
                  </div>
                } @else {
                  <p class="small muted">{{ 'fcal.noReturnFares' | t }}</p>
                }
                <div class="td-card">
                  <app-fare-months [days]="returnDays()" [currency]="f.currency" [minDate]="f.fare.date" [selected]="returnDate()"
                                   [rangeStart]="f.fare.date" [rangeEnd]="returnDate()" [tips]="false" (picked)="pickReturn($event.date)" />
                </div>
              }
            </section>
          }

          @if (mode() === 'oneway' || (mode() === 'round' && returnDate())) {
            <section id="td-options">
              <div class="td-total">
                <span>
                  @if (mode() === 'round') {
                    <strong>{{ f.fare.date | ldate: 'd MMM' }} – {{ returnDate() | ldate: 'd MMM' }}</strong> · {{ 'unit.days' | t: { count: tripDays(returnDate()!) } }}
                  } @else {
                    <strong>{{ f.fare.date | ldate: 'EEE, d MMM' }}</strong> · {{ 'fcal.oneWay' | t }}
                  }
                </span>
                <span class="td-pax">
                  <button type="button" (click)="setAdults(adults() - 1)" [disabled]="adults() <= 1" [attr.aria-label]="'fcal.lessTravelers' | t">−</button>
                  <span>{{ 'fcal.travelers' | t: { count: adults() } }}</span>
                  <button type="button" (click)="setAdults(adults() + 1)" [disabled]="adults() >= 9" [attr.aria-label]="'fcal.moreTravelers' | t">+</button>
                </span>
              </div>
              <div style="margin-top: 1rem">
                @if (optionsLoading()) {
                  <p class="muted small"><span class="spinner" aria-hidden="true"></span> {{ 'fcal.optionsLoading' | t }}</p>
                  <div class="td-skeleton"></div>
                } @else if (options(); as o) {
                  <app-flight-options [result]="o" />
                }
                <app-error-banner [error]="error()" />
              </div>
            </section>
          }
        </div>
      }
    </dialog>
  `,
})
export class TripDialogComponent implements OnDestroy {
  readonly flight = input<PickedFlight | null>(null);
  readonly closed = output<void>();

  private readonly api = inject(ApiService);
  private readonly i18n = inject(I18nService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly mode = signal<Mode>('ask');
  protected readonly returnDays = signal<FlightCalendarDayDto[]>([]);
  protected readonly returnLoading = signal(false);
  protected readonly returnDate = signal<string | null>(null);
  protected readonly adults = signal(1);
  protected readonly options = signal<FlightOptionsDto | null>(null);
  protected readonly optionsLoading = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  /** The cheapest days back within a month of the departure, in date order. */
  protected readonly bestReturns = computed(() => {
    const dep = this.flight()?.fare.date;
    if (!dep) return [];
    const limit = toIso(addDays(dep, 30));
    return this.returnDays()
      .filter((d) => d.date > dep && d.date <= limit)
      .sort((a, b) => a.price - b.price)
      .slice(0, 8)
      .sort((a, b) => a.date.localeCompare(b.date));
  });

  private returnRequest?: Subscription;
  private optionsRequest?: Subscription;
  private returnKey = '';

  constructor() {
    // Every new day picked starts with the question; the dialog opens as a modal.
    effect(() => {
      const f = this.flight();
      const dialog = this.dialog().nativeElement;
      if (!f) {
        if (dialog.open) dialog.close();
        return;
      }
      this.mode.set('ask');
      this.returnDate.set(null);
      this.options.set(null);
      this.error.set(null);
      if (!dialog.open) dialog.showModal();
      dialog.querySelector('.td-body')?.scrollTo({ top: 0 });
    });
  }

  ngOnDestroy(): void {
    this.returnRequest?.unsubscribe();
    this.optionsRequest?.unsubscribe();
  }

  protected close(): void {
    this.dialog().nativeElement.close();
  }

  /** A click on the dimmed backdrop (outside the dialog box) closes it. */
  protected backdropClick(event: MouseEvent): void {
    const box = this.dialog().nativeElement.getBoundingClientRect();
    const inside = event.clientX >= box.left && event.clientX <= box.right && event.clientY >= box.top && event.clientY <= box.bottom;
    if (!inside && event.target === this.dialog().nativeElement) this.close();
  }

  protected setMode(mode: Mode): void {
    this.mode.set(mode);
    if (mode === 'round') this.loadReturnCalendar();
    this.loadOptions();
  }

  protected pickReturn(date: string): void {
    this.returnDate.set(date);
    this.loadOptions();
    setTimeout(() => this.dialog().nativeElement.querySelector('#td-options')?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 60);
  }

  protected setAdults(n: number): void {
    this.adults.set(Math.min(9, Math.max(1, n)));
    this.loadOptions();
  }

  protected tripDays(returnDate: string): number {
    const dep = this.flight()?.fare.date;
    return dep ? Math.round((Date.parse(returnDate) - Date.parse(dep)) / 86_400_000) + 1 : 0;
  }

  protected money(value: number): string {
    const currency = this.flight()?.currency ?? 'EUR';
    return new Intl.NumberFormat(this.i18n.intlLocale(), { style: 'currency', currency, maximumFractionDigits: 0 }).format(value);
  }

  private loadReturnCalendar(): void {
    const f = this.flight();
    if (!f) return;
    const key = `${f.toCode}|${f.fromCode}|${f.currency}`;
    if (key === this.returnKey && (this.returnDays().length || this.returnLoading())) return;
    this.returnKey = key;
    this.returnRequest?.unsubscribe();
    this.returnLoading.set(true);
    this.returnRequest = this.api.getFlightCalendar(f.toCode, f.fromCode, f.currency, false).subscribe({
      next: (r) => {
        this.returnDays.set(r.routes[0]?.days ?? []);
        this.returnLoading.set(false);
      },
      error: () => {
        this.returnDays.set([]);
        this.returnLoading.set(false);
      },
    });
  }

  private loadOptions(): void {
    const f = this.flight();
    this.optionsRequest?.unsubscribe();
    this.options.set(null);
    this.error.set(null);
    const ret = this.mode() === 'round' ? this.returnDate() : null;
    if (!f || this.mode() === 'ask' || (this.mode() === 'round' && !ret)) {
      this.optionsLoading.set(false);
      return;
    }
    this.optionsLoading.set(true);
    this.optionsRequest = this.api.getFlightOptions(f.fromCode, f.toCode, f.fare.date, ret, this.adults(), f.currency).subscribe({
      next: (r) => {
        this.options.set(r);
        this.optionsLoading.set(false);
      },
      error: (e) => {
        this.error.set(toApiError(e));
        this.optionsLoading.set(false);
      },
    });
  }
}

function addDays(iso: string, days: number): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d + days);
}

function toIso(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}
