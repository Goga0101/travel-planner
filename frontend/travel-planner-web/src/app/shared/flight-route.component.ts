import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { FlightLegSummaryDto, FlightTicket } from '../core/models';
import { LocalDatePipe, MinutesPipe, MoneyPipe, StopsPipe } from './pipes';
import { ExternalLinkComponent } from './external-link.component';
import { AirlineComponent } from './airline.component';

/**
 * "Tbilisi (TBS) → London (LTN → STN, airport change) → Barcelona (BCN)": the connection cities of one direction.
 * Consecutive airports of the same city are one stop with an airport change.
 */
@Component({
  selector: 'app-flight-route',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="route">
      <strong>{{ leg().fromCity ?? leg().from }}</strong> ({{ leg().from }})
      @for (s of stops(); track $index) {
        → <span class="route-via">{{ s }}</span>
      }
      → <strong>{{ leg().toCity ?? leg().to }}</strong> ({{ leg().to }})
    </span>
  `,
})
export class FlightRouteComponent {
  readonly leg = input.required<FlightLegSummaryDto>();
  private readonly i18n = inject(I18nService);

  protected readonly stops = computed(() => viaLabels(this.leg(), this.i18n));
}

/** Connection stops of a leg, consecutive airports of one city merged into an "airport change". */
export function viaLabels(leg: FlightLegSummaryDto, i18n: I18nService): string[] {
  const groups: { city: string; codes: string[] }[] = [];
  for (const v of leg.via ?? []) {
    const last = groups.at(-1);
    if (last && last.city === v.city) last.codes.push(v.iata);
    else groups.push({ city: v.city, codes: [v.iata] });
  }
  return groups.map((g) =>
    g.codes.length > 1
      ? `${g.city} (${i18n.t('flight.airportChange', { from: g.codes[0], to: g.codes.at(-1)! })})`
      : g.city === g.codes[0] ? g.city : `${g.city} (${g.codes[0]})`,
  );
}

/**
 * One direction as a horizontal timeline: departure code and time, the connection cities on the line, arrival code and time.
 * Separate-ticket connections use the dashed amber line of the design system.
 */
@Component({
  selector: 'app-flight-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LocalDatePipe, MinutesPipe, StopsPipe, AirlineComponent],
  template: `
    @let l = leg();
    <div class="timeline" [class.self]="selfTransfer() && l.stops > 0">
      <div class="tl-end">
        <span class="tl-code">{{ l.from }}</span>
        <span class="tl-time">{{ l.departureLocal | ldate: 'HH:mm' }}</span>
        <span class="tl-city">{{ l.fromCity ?? l.from }} · {{ l.departureLocal | ldate: 'd MMM' }}</span>
      </div>
      <div class="tl-mid">
        <span class="tl-label">
          @if (stops().length) { {{ stops().join(' · ') }} } @else { {{ l.stops | stops }} }
        </span>
        <span class="tl-line" aria-hidden="true">
          @for (s of dots(); track $index) { <span class="tl-dot"></span> }
        </span>
        <!-- The stops are already above the line: below it the time in the air and who flies it. -->
        <span class="tl-meta">
          @if (l.durationMinutes) { {{ l.durationMinutes | minutes }} · }
          @if (l.carriers.length) {
            @for (c of l.carriers; track c) { @if (!$first) { , }<app-airline [code]="c" /> }
          } @else { {{ l.stops | stops }} }
        </span>
      </div>
      <div class="tl-end tl-right">
        <span class="tl-code">{{ l.to }}</span>
        <span class="tl-time">
          @if (l.arrivalLocal) { {{ l.arrivalLocal | ldate: 'HH:mm' }} @if (dayShift() > 0) { <sup>+{{ dayShift() }}</sup> } } @else { – }
        </span>
        <span class="tl-city">{{ l.toCity ?? l.to }}</span>
      </div>
    </div>
  `,
})
export class FlightTimelineComponent {
  readonly leg = input.required<FlightLegSummaryDto>();
  readonly selfTransfer = input(false);
  private readonly i18n = inject(I18nService);

  protected readonly stops = computed(() => viaLabels(this.leg(), this.i18n));
  protected readonly dots = computed(() => Array.from({ length: Math.min(this.leg().stops, 4) }));
  /** Days between departure and arrival dates (local), e.g. +1 for an overnight flight. */
  protected readonly dayShift = computed(() => {
    const l = this.leg();
    if (!l.arrivalLocal) return 0;
    const day = (s: string) => Date.UTC(+s.slice(0, 4), +s.slice(5, 7) - 1, +s.slice(8, 10));
    return Math.round((day(l.arrivalLocal) - day(l.departureLocal)) / 86_400_000);
  });
}

/** The separately booked tickets of a combined offer, each with its own booking link. */
@Component({
  selector: 'app-flight-tickets',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, LocalDatePipe, MoneyPipe, ExternalLinkComponent, AirlineComponent],
  template: `
    @if (selfTransfer()) {
      <p class="small text-warn"><span class="badge-self">{{ 'flight.selfTransferBadge' | t }}</span> {{ 'flight.selfTransferWarning' | t }}</p>
    }
    <ol class="tickets" [attr.aria-label]="'flight.tickets' | t">
      @for (t of tickets(); track $index) {
        <li>
          <span>
            <strong>{{ (t.isReturn ? 'flight.ticketReturn' : 'flight.ticketOutbound') | t }}:</strong>
            {{ t.from }} → {{ t.to }} · {{ t.departureLocal | ldate: 'EEE d MMM HH:mm' }}
            @if (t.carrier) { · <app-airline [code]="t.carrier" /> }
            @if (t.arrivalLocal) { – {{ t.arrivalLocal | ldate: 'HH:mm' }} }
            @if (overnight($index)) { · <em>{{ 'flight.overnight' | t }}</em> }
            · {{ t.price | money: t.currency }}
          </span>
          <app-external-link [href]="t.bookingUrl" [label]="'flight.bookTicket' | t: { from: t.from, to: t.to }" />
        </li>
      }
    </ol>
  `,
})
export class FlightTicketsComponent {
  readonly tickets = input.required<FlightTicket[]>();
  readonly selfTransfer = input(false);

  /** The next ticket of the same direction leaves on a later day than this one lands. */
  protected overnight(index: number): boolean {
    const list = this.tickets();
    const t = list[index];
    const next = list[index + 1];
    if (!next || next.isReturn !== t.isReturn || !t.arrivalLocal) return false;
    return next.departureLocal.slice(0, 10) > t.arrivalLocal.slice(0, 10);
  }
}
