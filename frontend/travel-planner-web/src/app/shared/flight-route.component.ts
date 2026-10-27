import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { FlightLegSummaryDto, FlightTicket } from '../core/models';
import { LocalDatePipe, MoneyPipe } from './pipes';
import { ExternalLinkComponent } from './external-link.component';

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

  protected readonly stops = computed(() => {
    const groups: { city: string; codes: string[] }[] = [];
    for (const v of this.leg().via ?? []) {
      const last = groups.at(-1);
      if (last && last.city === v.city) last.codes.push(v.iata);
      else groups.push({ city: v.city, codes: [v.iata] });
    }
    return groups.map((g) =>
      g.codes.length > 1
        ? `${g.city} (${this.i18n.t('flight.airportChange', { from: g.codes[0], to: g.codes.at(-1)! })})`
        : g.city === g.codes[0] ? g.city : `${g.city} (${g.codes[0]})`,
    );
  });
}

/** The separately booked tickets of a combined offer, each with its own booking link. */
@Component({
  selector: 'app-flight-tickets',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, LocalDatePipe, MoneyPipe, ExternalLinkComponent],
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
