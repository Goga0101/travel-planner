import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { TranslationKey } from '../../core/i18n/en';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { FlightOptionDto, FlightOptionKind, FlightOptionsDto } from '../../core/models';
import { ExternalLinkComponent } from '../../shared/external-link.component';
import { FlightTicketsComponent, FlightTimelineComponent } from '../../shared/flight-route.component';
import { MinutesPipe, MoneyPipe } from '../../shared/pipes';

type Sort = 'price' | 'duration' | 'departure';
type TicketFilter = 'all' | 'single' | 'separate';
const PAGE = 8;

const minutes = (o: FlightOptionDto) => (o.outbound.durationMinutes ?? 1e6) + (o.inbound ? o.inbound.durationMinutes ?? 1e6 : 0);
const stops = (o: FlightOptionDto) => Math.max(o.outbound.stops, o.inbound?.stops ?? 0);
const single = (o: FlightOptionDto) => o.tickets.length <= 1;

/** The flights for the chosen dates as cards: price, both directions as timelines, and the booking link of every ticket. */
@Component({
  selector: 'app-flight-options',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, ExternalLinkComponent, FlightTimelineComponent, FlightTicketsComponent, MoneyPipe, MinutesPipe],
  styles: `
    :host { display: block; }
    .fo-highlights { display: grid; gap: 0.6rem; grid-template-columns: repeat(auto-fit, minmax(min(100%, 180px), 1fr)); margin-bottom: 1rem; }
    .fo-hl { border: 1px solid var(--border); border-radius: var(--radius-md); padding: 0.65rem 0.85rem; background: var(--surface); text-align: left;
      font: inherit; color: inherit; cursor: pointer; display: grid; gap: 0.1rem; }
    .fo-hl:hover { border-color: var(--border-strong); }
    .fo-hl.active { border-color: var(--primary); box-shadow: 0 0 0 3px var(--primary-ring); }
    .fo-hl-label { font-size: 0.75rem; color: var(--muted); text-transform: uppercase; letter-spacing: 0.04em; }
    .fo-hl-value { font-size: 1.15rem; font-weight: 700; }
    .fo-filters { display: flex; flex-wrap: wrap; gap: 0.6rem 1.25rem; align-items: center; margin-bottom: 1rem; }
    .fo-list { display: grid; gap: 0.85rem; list-style: none; margin: 0; padding: 0; }
    .fo-card { border: 1px solid var(--border); border-radius: var(--radius-lg); background: var(--surface); box-shadow: var(--shadow);
      display: grid; grid-template-columns: minmax(0, 1fr) 13.5rem; overflow: hidden; transition: box-shadow 0.15s, border-color 0.15s; }
    .fo-card:hover { box-shadow: var(--shadow-hover); border-color: var(--border-strong); }
    .fo-legs { padding: 1rem 1.15rem; display: grid; gap: 0.9rem; }
    .fo-leg-head { display: flex; align-items: center; gap: 0.5rem; font-size: 0.78rem; color: var(--muted); margin-bottom: 0.35rem; }
    .fo-dir { font-weight: 700; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-2); }
    .fo-side { padding: 1rem 1.15rem; border-left: 1px dashed var(--border); background: var(--surface-tint);
      display: flex; flex-direction: column; justify-content: center; gap: 0.45rem; }
    .fo-price { font-size: 1.55rem; font-weight: 800; line-height: 1.1; }
    .fo-badge { align-self: flex-start; font-size: 0.72rem; font-weight: 700; padding: 0.15rem 0.55rem; border-radius: 999px;
      background: var(--primary-soft); color: var(--primary-deep); }
    .fo-badge.separate { background: var(--warn-soft); color: var(--warn); }
    .fo-side .btn { width: 100%; justify-content: center; }
    .fo-tickets { grid-column: 1 / -1; border-top: 1px solid var(--border); padding: 0.4rem 1.15rem 0.9rem; }
    .fo-tickets summary { cursor: pointer; font-weight: 600; font-size: 0.88rem; padding: 0.45rem 0; }
    .fo-empty { text-align: center; padding: 1.5rem 1rem; }
    .fo-more { margin-top: 0.9rem; display: flex; flex-wrap: wrap; gap: 0.75rem; justify-content: space-between; align-items: center; }
    @media (max-width: 720px) {
      .fo-card { grid-template-columns: 1fr; }
      .fo-side { border-left: 0; border-top: 1px dashed var(--border); }
    }
  `,
  template: `
    @let r = result();
    @if (r.options.length) {
      <div class="fo-highlights">
        <button type="button" class="fo-hl" [class.active]="sort() === 'price'" (click)="sort.set('price')">
          <span class="fo-hl-label">{{ 'fcal.cheapest' | t }}</span>
          <span class="fo-hl-value">{{ cheapest()!.price | money: cheapest()!.currency }}</span>
        </button>
        <button type="button" class="fo-hl" [class.active]="sort() === 'duration'" (click)="sort.set('duration')">
          <span class="fo-hl-label">{{ 'fcal.fastest' | t }}</span>
          <span class="fo-hl-value">{{ fastestMinutes() | minutes }}</span>
        </button>
        <button type="button" class="fo-hl" [class.active]="sort() === 'departure'" (click)="sort.set('departure')">
          <span class="fo-hl-label">{{ 'fcal.earliest' | t }}</span>
          <span class="fo-hl-value">{{ earliest() }}</span>
        </button>
      </div>

      <div class="fo-filters">
        <div class="pill-group" role="radiogroup" [attr.aria-label]="'flights.type' | t">
          @for (o of ticketOptions; track o.value) {
            @if (o.value === 'all' || count(o.value) > 0) {
            <button type="button" role="radio" class="pill-btn" [class.active]="tickets() === o.value" [attr.aria-checked]="tickets() === o.value"
                    (click)="tickets.set(o.value); shown.set(PAGE)">{{ o.label | t }} ({{ count(o.value) }})</button>
            }
          }
        </div>
        <div class="pill-group" role="radiogroup" [attr.aria-label]="'search.maxStops' | t">
          @for (o of stopOptions; track o.label + o.value) {
            <button type="button" role="radio" class="pill-btn" [class.active]="maxStops() === o.value" [attr.aria-checked]="maxStops() === o.value"
                    (click)="maxStops.set(o.value); shown.set(PAGE)">{{ o.label | t: { count: o.value ?? 0 } }}</button>
          }
        </div>
      </div>

      @if (visible().length) {
        <ul class="fo-list">
          @for (o of visible().slice(0, shown()); track $index) {
            <li class="fo-card">
              <div class="fo-legs">
                <div>
                  <div class="fo-leg-head"><span class="fo-dir">{{ (o.inbound ? 'fcal.outbound' : 'fcal.oneWayLeg') | t }}</span></div>
                  <app-flight-timeline [leg]="o.outbound" [selfTransfer]="o.kind === 'SelfTransfer'" />
                </div>
                @if (o.inbound; as back) {
                  <div>
                    <div class="fo-leg-head"><span class="fo-dir">{{ 'fcal.inbound' | t }}</span></div>
                    <app-flight-timeline [leg]="back" [selfTransfer]="o.kind === 'SelfTransfer'" />
                  </div>
                }
              </div>
              <div class="fo-side">
                <span class="fo-badge" [class.separate]="o.tickets.length > 1">{{ kindKey(o.kind) | t }}</span>
                <span class="fo-price">{{ o.price | money: o.currency }}</span>
                @if (r.adults > 1) {
                  <span class="small muted">{{ 'fcal.perTraveler' | t: { price: (o.price / r.adults | money: o.currency), count: r.adults } }}</span>
                }
                @if (o.tickets.length > 1) {
                  <span class="small muted">{{ 'flights.ticketsCount' | t: { count: o.tickets.length } }} · {{ 'fcal.eachLink' | t }}</span>
                } @else {
                  <app-external-link [href]="o.bookingUrl" [label]="'fcal.bookNow' | t" [fallback]="'flights.noLink' | t" [button]="true" />
                }
              </div>
              @if (o.tickets.length > 1) {
                <details class="fo-tickets" [open]="$first">
                  <summary>{{ 'fcal.showTickets' | t: { count: o.tickets.length } }}</summary>
                  <app-flight-tickets [tickets]="o.tickets" [selfTransfer]="o.kind === 'SelfTransfer'" />
                </details>
              }
            </li>
          }
        </ul>
      } @else {
        <p class="muted fo-empty">{{ 'flights.none' | t }}</p>
      }

      <div class="fo-more">
        @if (visible().length > shown()) {
          <button type="button" class="btn btn-ghost" (click)="shown.set(shown() + PAGE)">{{ 'flights.showMore' | t: { count: visible().length - shown() } }}</button>
        } @else { <span></span> }
        <app-external-link [href]="r.searchUrl" [label]="'fcal.liveSearch' | t" />
      </div>
    } @else {
      <div class="fo-empty">
        <p><strong>{{ 'fcal.noOptions' | t }}</strong></p>
        <p class="muted small">{{ 'fcal.noOptionsHint' | t }}</p>
        <app-external-link [href]="r.searchUrl" [label]="'fcal.liveSearch' | t" [button]="true" />
      </div>
    }
  `,
})
export class FlightOptionsComponent {
  readonly result = input.required<FlightOptionsDto>();

  protected readonly PAGE = PAGE;
  protected readonly sort = signal<Sort>('price');
  protected readonly tickets = signal<TicketFilter>('all');
  protected readonly maxStops = signal<number | null>(null);
  protected readonly shown = signal(PAGE);

  protected readonly ticketOptions: { value: TicketFilter; label: TranslationKey }[] = [
    { value: 'all', label: 'common.all' },
    { value: 'single', label: 'flights.typeSingle' },
    { value: 'separate', label: 'flights.typeSelf' },
  ];
  protected readonly stopOptions: { value: number | null; label: TranslationKey }[] = [
    { value: null, label: 'common.all' },
    { value: 0, label: 'search.directOnly' },
    { value: 1, label: 'search.upToStops' },
    { value: 2, label: 'search.upToStops' },
  ];

  protected readonly cheapest = computed(() => [...this.result().options].sort((a, b) => a.price - b.price)[0]);
  protected readonly fastestMinutes = computed(() => Math.min(...this.result().options.map(minutes)));
  protected readonly earliest = computed(() =>
    this.result().options.map((o) => o.outbound.departureLocal.slice(11, 16)).sort()[0] ?? '–');

  protected readonly visible = computed(() => {
    const filter = this.tickets();
    const max = this.maxStops();
    const list = this.result().options.filter(
      (o) => (filter === 'all' || (filter === 'single') === single(o)) && (max === null || stops(o) <= max),
    );
    const by: Record<Sort, (a: FlightOptionDto, b: FlightOptionDto) => number> = {
      price: (a, b) => a.price - b.price,
      duration: (a, b) => minutes(a) - minutes(b) || a.price - b.price,
      departure: (a, b) => a.outbound.departureLocal.slice(11).localeCompare(b.outbound.departureLocal.slice(11)) || a.price - b.price,
    };
    return [...list].sort(by[this.sort()]);
  });

  protected count(filter: TicketFilter): number {
    return this.result().options.filter((o) => filter === 'all' || (filter === 'single') === single(o)).length;
  }

  protected kindKey(kind: FlightOptionKind): TranslationKey {
    return `fcal.kind.${kind}`;
  }
}
