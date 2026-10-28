import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TransitItineraryDto, TransitLegDto } from '../../core/models';
import { IconComponent, IconName } from '../../shared/icon.component';
import { MinutesPipe, MoneyPipe } from '../../shared/pipes';

export const MODE_ICONS: Record<string, IconName> = {
  WALK: 'walk',
  BUS: 'bus',
  COACH: 'bus',
  TRAM: 'tram',
  SUBWAY: 'metro',
  METRO: 'metro',
  RAIL: 'train',
  SUBURBAN: 'train',
  REGIONAL_RAIL: 'train',
  REGIONAL_FAST_RAIL: 'train',
  FERRY: 'ship',
  metro: 'metro',
  train: 'train',
  tram: 'tram',
  bus: 'bus',
  express_bus: 'bus',
  ferry: 'ship',
};

export function modeIcon(mode: string): IconName {
  return MODE_ICONS[mode] ?? 'train';
}

/** One public-transport journey: line chips and the steps, each ride with its ticket and where to buy it. */
@Component({
  selector: 'li[app-itinerary]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, MinutesPipe, MoneyPipe],
  template: `
      @let it = itinerary();
      <div class="route-option-head">
        <strong class="route-time">{{ it.departureTime }} → {{ it.arrivalTime }}</strong>
        <span class="meta-chip">{{ it.durationMinutes | minutes }}</span>
        <span class="meta-chip">{{ it.transfers === 0 ? ('route.noTransfer' | t) : ('route.transfers' | t: { count: it.transfers }) }}</span>
        @if (best()) { <span class="badge-best">{{ 'route.fastest' | t }}</span> }
      </div>
      <div class="route-chips">
        @for (l of transitLegs(it.legs); track $index) {
          @if (ticketLink(l); as href) {
            <a class="line-chip is-link" [attr.data-mode]="l.mode" [href]="href" target="_blank" rel="noopener noreferrer nofollow"
               [title]="'route.buyTicketFor' | t: { line: (l.line ?? modeLabel(l.mode)) }">
              <app-icon [name]="icon(l.mode)" [size]="13" /> {{ l.line ?? modeLabel(l.mode) }} <app-icon name="ticket" [size]="12" />
            </a>
          } @else {
            <span class="line-chip" [attr.data-mode]="l.mode"><app-icon [name]="icon(l.mode)" [size]="13" /> {{ l.line ?? modeLabel(l.mode) }}</span>
          }
        }
      </div>
      <ol class="route-steps">
        @for (l of it.legs; track $index) {
          <li class="route-step" [attr.data-mode]="l.mode">
            <span class="step-time">{{ l.departureTime }}</span>
            @if (ticketLink(l); as href) {
              <a class="step-icon is-link" [attr.data-mode]="l.mode" [href]="href" target="_blank" rel="noopener noreferrer nofollow"
                 [title]="'route.buyTicketFor' | t: { line: (l.line ?? modeLabel(l.mode)) }"
                 [attr.aria-label]="'route.buyTicketFor' | t: { line: (l.line ?? modeLabel(l.mode)) }">
                <app-icon [name]="icon(l.mode)" [size]="17" />
              </a>
            } @else {
              <span class="step-icon" [attr.data-mode]="l.mode"><app-icon [name]="icon(l.mode)" [size]="17" /></span>
            }
            <span class="step-text">
              @if (l.mode === 'WALK') {
                {{ 'route.walk' | t: { minutes: l.durationMinutes } }}@if (l.to) { {{ 'route.walkTo' | t: { place: l.to } }} }
              } @else {
                <strong>{{ modeLabel(l.mode) }} {{ l.line }}</strong>
                @if (l.headsign) { <span class="muted"> {{ 'route.towards' | t: { headsign: l.headsign } }}</span> }
                <br /><span class="small">{{ l.from }} → {{ l.to }} · {{ l.durationMinutes | minutes }}@if (l.stops) { · {{ 'route.stops' | t: { count: l.stops } }} }</span>
                @if (stopLink(l); as href) {
                  <br /><a class="stop-link" [href]="href" target="_blank" rel="noopener noreferrer">
                    <app-icon name="walk" [size]="13" /> {{ 'route.toStop' | t: { stop: l.from } }} <app-icon name="external" [size]="11" />
                  </a>
                }
                @if (l.agency) { <br /><span class="small muted">{{ l.agency }}</span> }
                @if (l.ticket; as tk) {
                  <span class="leg-ticket" [class.online]="tk.buyOnline === true" [class.offline]="tk.buyOnline === false">
                    <app-icon name="ticket" [size]="14" />
                    <span>
                      <strong>{{ tk.name ?? ('route.ticket' | t) }}</strong>
                      @if (tk.price !== undefined && tk.price !== null) { · {{ tk.price | money: tk.currency }} }
                      @if (tk.buyOnline === true) { · {{ 'route.buyOnlineYes' | t }} }
                      @if (tk.buyOnline === false) { · {{ 'route.buyOnlineNo' | t }} }
                      @if (tk.note) { <br /><span class="muted">{{ tk.note }}</span> }
                    </span>
                    @if (tk.buyUrl) {
                      <a class="leg-buy" [href]="tk.buyUrl" target="_blank" rel="noopener noreferrer nofollow">{{ 'route.buy' | t }} <app-icon name="external" [size]="12" /></a>
                    } @else if (tk.infoUrl) {
                      <a class="leg-info" [href]="tk.infoUrl" target="_blank" rel="noopener noreferrer nofollow">{{ 'guide.official' | t }} <app-icon name="external" [size]="12" /></a>
                    }
                  </span>
                }
              }
            </span>
          </li>
        }
      </ol>
  `,
})
export class ItineraryComponent {
  readonly itinerary = input.required<TransitItineraryDto>();
  readonly best = input(false);

  private readonly i18n = inject(I18nService);

  /** Where tapping a ride goes: the ticket purchase page, else the operator's official page. */
  protected ticketLink(l: TransitLegDto): string | null {
    return l.ticket?.buyUrl ?? l.ticket?.infoUrl ?? null;
  }

  /** Google Maps walking directions from where the visitor is now to the stop where this ride starts. */
  protected stopLink(l: TransitLegDto): string | null {
    const p = l.path;
    return p ? `https://www.google.com/maps/dir/?api=1&destination=${p.fromLatitude.toFixed(6)},${p.fromLongitude.toFixed(6)}&travelmode=walking` : null;
  }

  protected transitLegs(legs: TransitLegDto[]): TransitLegDto[] {
    return legs.filter((l) => l.mode !== 'WALK');
  }

  protected icon(mode: string): IconName {
    return modeIcon(mode);
  }

  protected modeLabel(mode: string): string {
    const key = `route.mode.${mode}` as TranslationKey;
    const text = this.i18n.t(key);
    return text === key ? mode.toLowerCase() : text;
  }
}
