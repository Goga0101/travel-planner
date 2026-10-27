import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiService } from '../../core/api.service';
import { ApiError, toApiError } from '../../core/errors';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { StayRouteDto, TransitLegDto } from '../../core/models';
import { SelectedStayService } from '../../core/selected-stay.service';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { ExternalLinkComponent } from '../../shared/external-link.component';
import { IconComponent, IconName } from '../../shared/icon.component';
import { LocalDatePipe, MinutesPipe, MoneyPipe } from '../../shared/pipes';

const MODE_ICONS: Record<string, IconName> = {
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

/**
 * Getting from the arrival airport to the chosen hotel (or the city centre) and back: step-by-step public-transport
 * options from open timetables, taxi and ride-hailing links, and the city's ticket guide (where to buy, prices).
 */
@Component({
  selector: 'app-stay-route',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, ExternalLinkComponent, MinutesPipe, MoneyPipe, LocalDatePipe, ErrorBannerComponent],
  template: `
    <section class="card stay-route" id="route" aria-labelledby="route-title">
      <div class="section-head">
        <h2 id="route-title"><app-icon name="train" /> {{ 'route.title' | t }}</h2>
        @if (stay(); as st) {
          <button type="button" class="text-link small" (click)="stays.clear(planId())"><app-icon name="x" [size]="14" /> {{ 'route.clearStay' | t }}</button>
        }
      </div>
      @if (!stay()) { <p class="muted small route-hint"><app-icon name="info" [size]="14" /> {{ 'route.pickHotel' | t }}</p> }

      <app-error-banner [error]="error()" />
      @if (loading() && !data()) { <p class="muted"><span class="spinner" aria-hidden="true"></span> {{ 'route.loading' | t }}</p> }

      @if (data(); as d) {
        @if (d.toStay; as dir) {
          <div class="direction-tabs pill-group" role="tablist">
            <button type="button" role="tab" class="pill-btn" [class.active]="direction() === 'in'" [attr.aria-selected]="direction() === 'in'" (click)="direction.set('in')">
              {{ d.airportCode }} → {{ d.stayName }}
            </button>
            @if (d.toAirport) {
              <button type="button" role="tab" class="pill-btn" [class.active]="direction() === 'out'" [attr.aria-selected]="direction() === 'out'" (click)="direction.set('out')">
                {{ d.stayName }} → {{ d.airportCode }}
              </button>
            }
          </div>
          @let r = direction() === 'out' && d.toAirport ? d.toAirport : dir;
          <p class="small muted">
            {{ (direction() === 'out' ? 'route.leaveAt' : 'route.arriveAt') | t: { time: r.wishedTime } }}
            @if (r.itineraries.length && !r.onTravelDate) { · {{ 'route.timetableNote' | t: { date: (r.timetableDate | ldate: 'EEE d MMM') } }} }
          </p>

          @if (r.itineraries.length) {
            <ol class="route-options">
              @for (it of r.itineraries; track $index; let first = $first) {
                <li class="route-option" [class.best]="first">
                  <div class="route-option-head">
                    <strong class="route-time">{{ it.departureTime }} → {{ it.arrivalTime }}</strong>
                    <span class="meta-chip">{{ it.durationMinutes | minutes }}</span>
                    <span class="meta-chip">{{ it.transfers === 0 ? ('route.noTransfer' | t) : ('route.transfers' | t: { count: it.transfers }) }}</span>
                    @if (first) { <span class="badge-best">{{ 'route.fastest' | t }}</span> }
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
                </li>
              }
            </ol>
          } @else {
            <p class="muted small">{{ 'route.noJourneys' | t }}</p>
          }

          <div class="route-alt">
            <app-external-link [href]="r.googleTransitUrl" [label]="'route.googleTransit' | t" />
            <app-external-link [href]="r.uberUrl" [label]="'route.uber' | t" />
            <app-external-link [href]="r.googleDriveUrl" [label]="'route.drive' | t" />
          </div>
          @if (d.routerName && r.itineraries.length) { <p class="small muted">{{ 'route.source' | t: { source: d.routerName } }}</p> }
        }

        @if (d.guide; as g) {
          <div class="ticket-guide" id="tickets">
            <h3><app-icon name="ticket" [size]="18" /> {{ 'guide.title' | t: { airport: g.airport } }}</h3>
            <div class="guide-badges">
              @if (g.contactless === true) { <span class="badge-ok"><app-icon name="check" [size]="13" /> {{ 'guide.contactlessYes' | t }}</span> }
              @if (g.contactless === false) { <span class="badge-self">{{ 'guide.contactlessNo' | t }}</span> }
            </div>
            <ul class="guide-options">
              @for (o of g.options; track $index) {
                <li class="guide-option">
                  <div class="guide-option-head">
                    <span class="step-icon"><app-icon [name]="icon(o.mode)" [size]="16" /></span>
                    <strong>{{ o.name }}</strong>
                    @if (o.price !== undefined && o.price !== null) { <span class="guide-price">{{ o.price | money: o.currency }}</span> }
                  </div>
                  @if (o.buyOnline !== undefined && o.buyOnline !== null) {
                    <span class="buy-badge" [class.online]="o.buyOnline" [class.offline]="!o.buyOnline">
                      {{ (o.buyOnline ? 'route.buyOnlineYes' : 'route.buyOnlineNo') | t }}
                    </span>
                  }
                  @if (o.from || o.to) { <p class="small muted">{{ o.from }}@if (o.to) { → {{ o.to }} }</p> }
                  <dl class="guide-facts small">
                    @if (o.ticket) { <div><dt>{{ 'guide.ticket' | t }}</dt><dd>{{ o.ticket }}</dd></div> }
                    @if (o.whereToBuy) { <div><dt>{{ 'guide.whereToBuy' | t }}</dt><dd>{{ o.whereToBuy }}</dd></div> }
                    @if (o.payment) { <div><dt>{{ 'guide.payment' | t }}</dt><dd>{{ o.payment }}</dd></div> }
                    @if (o.duration || o.frequency) { <div><dt>{{ 'guide.timing' | t }}</dt><dd>{{ o.duration }}@if (o.duration && o.frequency) { · }{{ o.frequency }}</dd></div> }
                  </dl>
                  @if (o.buyNote) { <p class="small">{{ o.buyNote }}</p> }
                  <div class="guide-links">
                    @if (o.buyUrl) {
                      <a class="btn btn-sm btn-deep" [href]="o.buyUrl" target="_blank" rel="noopener noreferrer nofollow">
                        <app-icon name="ticket" [size]="14" /> {{ 'route.buyOnline' | t }}
                      </a>
                    }
                    @if (o.url) { <app-external-link [href]="o.url" [label]="'guide.official' | t" /> }
                  </div>
                </li>
              }
            </ul>
            @if (g.cityBuyNote || g.cityBuyUrl) {
              <p class="small city-buy">
                <app-icon name="ticket" [size]="14" />
                <strong>{{ 'route.cityTickets' | t }}:</strong>
                @if (g.cityBuyOnline === true) { {{ 'route.buyOnlineYes' | t }} · }
                @if (g.cityBuyOnline === false) { {{ 'route.buyOnlineNo' | t }} · }
                {{ g.cityBuyNote }}
                @if (g.cityBuyUrl) { <app-external-link [href]="g.cityBuyUrl" [label]="'route.buy' | t" /> }
              </p>
            }
            @if (g.cityTicket || g.dayPass) {
              <div class="guide-fares small">
                @if (g.cityTicket; as f) { <span><strong>{{ 'guide.cityTicket' | t }}:</strong> {{ f.name }} @if (f.price !== undefined && f.price !== null) { · {{ f.price | money: f.currency }} }</span> }
                @if (g.dayPass; as f) { <span><strong>{{ 'guide.dayPass' | t }}:</strong> {{ f.name }} @if (f.price !== undefined && f.price !== null) { · {{ f.price | money: f.currency }} }</span> }
              </div>
            }
            @if (g.tips.length) {
              <ul class="guide-tips small">
                @for (tip of g.tips; track $index) { <li><app-icon name="info" [size]="14" /> {{ tip }}</li> }
              </ul>
            }
            @if (g.apps.length) {
              <p class="small">{{ 'guide.apps' | t }}:
                @for (a of g.apps; track a.name; let last = $last) { <app-external-link [href]="a.url" [label]="a.name" />@if (!last) { , } }
              </p>
            }
            <p class="small muted">{{ 'guide.sourceNote' | t: { date: g.checkedOn ?? '' } }}
              @for (s of g.sources; track s; let i = $index) { <app-external-link [href]="s" [label]="'[' + (i + 1) + ']'" /> }
            </p>
          </div>
        } @else {
          <p class="small muted">{{ 'guide.none' | t }}</p>
        }
      }
    </section>
  `,
})
export class StayRouteComponent {
  readonly planId = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  protected readonly stays = inject(SelectedStayService);

  protected readonly stay = computed(() => {
    this.stays.all();
    return this.stays.get(this.planId());
  });
  protected readonly data = signal<StayRouteDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly direction = signal<'in' | 'out'>('in');

  constructor() {
    effect((onCleanup) => {
      const id = this.planId();
      const stay = this.stay();
      this.i18n.lang();
      this.loading.set(true);
      this.error.set(null);
      const sub = this.api
        .getStayRoute(id, stay)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (d) => {
            this.data.set(d);
            this.loading.set(false);
          },
          error: (e) => {
            this.error.set(toApiError(e));
            this.loading.set(false);
          },
        });
      onCleanup(() => sub.unsubscribe());
    });
  }

  /** Where tapping a ride goes: the ticket purchase page, else the operator's official page. */
  protected ticketLink(l: TransitLegDto): string | null {
    return l.ticket?.buyUrl ?? l.ticket?.infoUrl ?? null;
  }

  protected transitLegs(legs: TransitLegDto[]): TransitLegDto[] {
    return legs.filter((l) => l.mode !== 'WALK');
  }

  protected icon(mode: string): IconName {
    return MODE_ICONS[mode] ?? 'train';
  }

  protected modeLabel(mode: string): string {
    const key = `route.mode.${mode}` as TranslationKey;
    const text = this.i18n.t(key);
    return text === key ? mode.toLowerCase() : text;
  }
}
