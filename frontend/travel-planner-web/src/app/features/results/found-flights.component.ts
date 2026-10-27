import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { TranslationKey } from '../../core/i18n/en';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { FoundFlightDto } from '../../core/models';
import { ExternalLinkComponent } from '../../shared/external-link.component';
import { FlightRouteComponent, FlightTicketsComponent } from '../../shared/flight-route.component';
import { LocalDatePipe, MinutesPipe, MoneyPipe, StopsPipe } from '../../shared/pipes';

type FlightSort = 'PriceAsc' | 'PriceDesc' | 'Duration' | 'Departure';
type TicketFilter = 'all' | 'single' | 'self';
const PAGE = 20;

const totalMinutes = (f: FoundFlightDto) => (f.outbound.durationMinutes ?? 1e6) + (f.inbound.durationMinutes ?? 1e6);
const maxStops = (f: FoundFlightDto) => Math.max(f.outbound.stops, f.inbound.stops);

/** Every flight the search found (not only those in plans), sortable cheapest/most expensive first and filterable. */
@Component({
  selector: 'app-found-flights',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, TranslatePipe, LocalDatePipe, MoneyPipe, MinutesPipe, StopsPipe, ExternalLinkComponent, FlightRouteComponent, FlightTicketsComponent],
  template: `
    @if (flights().length) {
      <section class="found-flights card" aria-labelledby="flights-title">
        <h2 id="flights-title">{{ 'flights.title' | t: { count: flights().length } }}</h2>
        <p class="muted small">{{ 'flights.lead' | t }}</p>

        <div class="flights-filters">
          <div class="field inline">
            <span class="label" id="flight-sort">{{ 'results.sortBy' | t }}</span>
            <div class="pill-group" role="radiogroup" aria-labelledby="flight-sort">
              @for (o of sortOptions; track o.value) {
                <button type="button" role="radio" class="pill-btn" [class.active]="sort() === o.value" [attr.aria-checked]="sort() === o.value"
                        (click)="sort.set(o.value); shown.set(PAGE)">{{ o.label | t }}</button>
              }
            </div>
          </div>
          <div class="field inline">
            <span class="label" id="flight-stops">{{ 'search.maxStops' | t }}</span>
            <div class="pill-group" role="radiogroup" aria-labelledby="flight-stops">
              @for (o of stopOptions; track o.value) {
                <button type="button" role="radio" class="pill-btn" [class.active]="stops() === o.value" [attr.aria-checked]="stops() === o.value"
                        (click)="stops.set(o.value)">{{ o.label | t: { count: o.value ?? 0 } }}</button>
              }
            </div>
          </div>
          <div class="field inline">
            <span class="label" id="flight-type">{{ 'flights.type' | t }}</span>
            <div class="pill-group" role="radiogroup" aria-labelledby="flight-type">
              @for (o of typeOptions; track o.value) {
                <button type="button" role="radio" class="pill-btn" [class.active]="type() === o.value" [attr.aria-checked]="type() === o.value"
                        (click)="type.set(o.value)">{{ o.label | t }}</button>
              }
            </div>
          </div>
          <div class="field inline">
            <label for="flight-max">{{ 'flights.maxPrice' | t }}</label>
            <input id="flight-max" type="number" min="0" [placeholder]="'common.any' | t" (input)="setMaxPrice($any($event.target).value)" />
          </div>
          @if (destinations().length > 1) {
            <div class="field inline">
              <label for="flight-dest">{{ 'results.destination' | t }}</label>
              <select id="flight-dest" [ngModel]="destination()" (ngModelChange)="destination.set($event)">
                <option value="">{{ 'common.all' | t }}</option>
                @for (d of destinations(); track d.id) { <option [value]="d.id">{{ d.city }}</option> }
              </select>
            </div>
          }
        </div>

        @if (visible().length) {
          <table class="flights-table">
            <thead>
              <tr>
                <th scope="col">{{ 'results.destination' | t }}</th>
                <th scope="col">{{ 'flights.route' | t }}</th>
                <th scope="col">{{ 'flights.price' | t }}</th>
                <th scope="col"><span class="visually-hidden">{{ 'flights.book' | t }}</span></th>
              </tr>
            </thead>
            <tbody>
              @for (f of visible().slice(0, shown()); track $index) {
                <tr [class.over]="!f.withinBudget">
                  <td>
                    <strong>{{ f.destination.city }}</strong>
                    <div class="small muted">{{ f.departureDate | ldate: 'd MMM' }} – {{ f.returnDate | ldate: 'd MMM' }}</div>
                    @if (f.selfTransfer) { <span class="badge-self">{{ 'flight.selfTransferBadge' | t }}</span> }
                  </td>
                  <td>
                    <div><app-flight-route [leg]="f.outbound" /></div>
                    <div class="small muted">{{ f.outbound.departureLocal | ldate: 'EEE d MMM HH:mm' }} · {{ f.outbound.durationMinutes | minutes }} · {{ f.outbound.stops | stops }}</div>
                    <div><app-flight-route [leg]="f.inbound" /></div>
                    <div class="small muted">{{ f.inbound.departureLocal | ldate: 'EEE d MMM HH:mm' }} · {{ f.inbound.durationMinutes | minutes }} · {{ f.inbound.stops | stops }}</div>
                  </td>
                  <td class="price">
                    {{ f.price | money: f.currency }}
                    @if (!f.withinBudget) { <div class="small">{{ 'flights.overBudget' | t }}</div> }
                  </td>
                  <td>
                    @if (f.tickets.length > 1) {
                      <details>
                        <summary>{{ 'flights.ticketsCount' | t: { count: f.tickets.length } }}</summary>
                        <app-flight-tickets [tickets]="f.tickets" [selfTransfer]="f.selfTransfer" />
                      </details>
                    } @else {
                      <app-external-link class="book-link" [href]="f.bookingUrl" [label]="'flights.book' | t" [fallback]="'flights.noLink' | t" />
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
          @if (visible().length > shown()) {
            <button type="button" class="btn btn-ghost" (click)="shown.set(shown() + PAGE)">
              {{ 'flights.showMore' | t: { count: visible().length - shown() } }}
            </button>
          }
        } @else {
          <p class="muted">{{ 'flights.none' | t }}</p>
        }
      </section>
    }
  `,
})
export class FoundFlightsComponent {
  readonly searchId = input.required<string>();
  /** The UI language: city names come localized from the API, so the list reloads when it changes. */
  readonly lang = input('');

  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly PAGE = PAGE;
  protected readonly flights = signal<FoundFlightDto[]>([]);
  protected readonly sort = signal<FlightSort>('PriceAsc');
  protected readonly maxPrice = signal<number | null>(null);
  protected readonly stops = signal<number | null>(null);
  protected readonly type = signal<TicketFilter>('all');
  protected readonly destination = signal('');
  protected readonly shown = signal(PAGE);

  protected readonly sortOptions: { value: FlightSort; label: TranslationKey }[] = [
    { value: 'PriceAsc', label: 'flights.sort.PriceAsc' },
    { value: 'PriceDesc', label: 'flights.sort.PriceDesc' },
    { value: 'Duration', label: 'flights.sort.Duration' },
    { value: 'Departure', label: 'flights.sort.Departure' },
  ];

  protected readonly stopOptions: { value: number | null; label: TranslationKey }[] = [
    { value: null, label: 'common.all' },
    { value: 0, label: 'search.directOnly' },
    { value: 1, label: 'search.upToStops' },
    { value: 2, label: 'search.upToStops' },
  ];
  protected readonly typeOptions: { value: TicketFilter; label: TranslationKey }[] = [
    { value: 'all', label: 'common.all' },
    { value: 'single', label: 'flights.typeSingle' },
    { value: 'self', label: 'flights.typeSelf' },
  ];

  protected readonly destinations = computed(() =>
    [...new Map(this.flights().map((f) => [f.destination.id, f.destination])).values()].sort((a, b) => a.city.localeCompare(b.city)),
  );

  protected readonly visible = computed(() => {
    const max = this.maxPrice();
    const stops = this.stops();
    const type = this.type();
    const dest = this.destination();
    const list = this.flights().filter(
      (f) =>
        (max === null || f.price <= max) &&
        (stops === null || maxStops(f) <= stops) &&
        (type === 'all' || (type === 'self') === f.selfTransfer) &&
        (!dest || f.destination.id === dest),
    );
    const by: Record<FlightSort, (a: FoundFlightDto, b: FoundFlightDto) => number> = {
      PriceAsc: (a, b) => a.price - b.price,
      PriceDesc: (a, b) => b.price - a.price,
      Duration: (a, b) => totalMinutes(a) - totalMinutes(b) || a.price - b.price,
      Departure: (a, b) => a.outbound.departureLocal.localeCompare(b.outbound.departureLocal) || a.price - b.price,
    };
    return [...list].sort(by[this.sort()]);
  });

  constructor() {
    effect(() => {
      const id = this.searchId();
      this.lang();
      this.api
        .getFoundFlights(id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({ next: (list) => this.flights.set(list), error: () => this.flights.set([]) });
    });
  }

  protected setMaxPrice(value: string): void {
    const n = Number(value);
    this.maxPrice.set(value === '' || !Number.isFinite(n) ? null : n);
    this.shown.set(PAGE);
  }
}
