import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nService } from '../core/i18n/i18n.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { LoungeProgram, LoungeService, PlaceMatch, matchPlaces } from '../core/lounges';
import { IconComponent } from './icon.component';

const PROGRAM_LABEL: Record<LoungeProgram, string> = {
  PriorityPass: 'Priority Pass',
  LoungeKey: 'LoungeKey',
  DragonPass: 'DragonPass',
  MastercardTravelPass: 'Mastercard Travel Pass',
  VisaAirportCompanion: 'Visa Airport Companion',
  BankOwn: 'TBC Concept',
  Other: 'Visa Kutaisi',
};

const KIND_ICON: Record<string, string> = { lounge: '🛋️', dining: '🍽️', spa: '💆', sleep: '😴' };

/** A lounge network as a small coloured badge. */
@Component({
  selector: 'app-program-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host { display: inline-flex; }
    span { font-size: 0.68rem; font-weight: 800; letter-spacing: 0.02em; padding: 0.12rem 0.5rem; border-radius: 999px; color: #fff; white-space: nowrap; }
    [data-p='PriorityPass'] { background: #6b1d3a; }
    [data-p='LoungeKey'] { background: #0f766e; }
    [data-p='DragonPass'] { background: #b91c1c; }
    [data-p='MastercardTravelPass'] { background: #ea580c; }
    [data-p='VisaAirportCompanion'] { background: #1a1f71; }
    [data-p='BankOwn'], [data-p='Other'] { background: #475569; }
  `,
  template: `<span [attr.data-p]="program()">{{ label() }}</span>`,
})
export class ProgramBadgeComponent {
  readonly program = input.required<LoungeProgram>();
  protected readonly label = computed(() => PROGRAM_LABEL[this.program()] ?? this.program());
}

/** One lounge / restaurant / spa: where it is, which networks it takes, and which of the visitor's cards open it. */
@Component({
  selector: 'app-lounge-place',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, ProgramBadgeComponent, IconComponent],
  styles: `
    :host { display: block; }
    .lp { display: grid; gap: 0.45rem; padding: 0.9rem 1rem; border-radius: var(--radius-md); background: var(--surface); border: 1px solid var(--border); height: 100%; }
    .lp.open { border-color: var(--primary); box-shadow: 0 0 0 3px var(--primary-ring); }
    .lp-head { display: flex; gap: 0.6rem; align-items: flex-start; }
    .lp-kind { font-size: 1.3rem; line-height: 1; }
    .lp-name { margin: 0; font-size: 1rem; }
    .lp-where { margin: 0.1rem 0 0; font-size: 0.8rem; color: var(--muted); }
    .lp-badges { display: flex; flex-wrap: wrap; gap: 0.3rem; }
    .lp-offer { font-size: 0.82rem; font-weight: 700; color: var(--stay-est-text, #b45309); }
    .lp-cards { display: flex; flex-wrap: wrap; gap: 0.3rem; align-items: center; font-size: 0.8rem; }
    .lp-card { padding: 0.12rem 0.5rem; border-radius: 999px; background: var(--primary-soft); color: var(--primary-deep); font-weight: 700; }
    .lp-none { font-size: 0.78rem; color: var(--muted); }
    .lp-source { font-size: 0.75rem; color: var(--muted); }
  `,
  template: `
    @let p = match().place;
    <article class="lp" [class.open]="match().cards.length > 0">
      <div class="lp-head">
        <span class="lp-kind" aria-hidden="true">{{ kindIcon() }}</span>
        <div>
          <h4 class="lp-name">{{ p.name }}</h4>
          <p class="lp-where">
            {{ $any('lounge.kind.' + p.kind) | t }}@if (p.terminal) { · {{ p.terminal }} }@if (p.schengen === 'schengen') { · Schengen }@if (p.schengen === 'non-schengen') { · non-Schengen }
            @if (p.hours) { · {{ p.hours }} }
            @if (p.location) { <br />{{ p.location }} }
          </p>
        </div>
      </div>
      <div class="lp-badges">@for (pr of p.programs; track pr) { <app-program-badge [program]="pr" /> }</div>
      @if (p.offer) { <span class="lp-offer">{{ p.offer }}</span> }
      @if (showCards()) {
        @if (match().cards.length) {
          <div class="lp-cards"><app-icon name="check" [size]="14" /> {{ 'lounge.yourCards' | t }}
            @for (c of match().cards; track c.id) { <span class="lp-card">{{ c.card }}</span> }
          </div>
        } @else {
          <span class="lp-none">{{ 'lounge.notYourCards' | t }}</span>
        }
      }
      <a class="lp-source" [href]="p.source" target="_blank" rel="noopener noreferrer nofollow">{{ 'lounge.source' | t }} ↗</a>
    </article>
  `,
})
export class LoungePlaceComponent {
  readonly match = input.required<PlaceMatch>();
  /** Whether the visitor picked cards (then each place says whether they open it). */
  readonly showCards = input(false);
  protected readonly kindIcon = computed(() => KIND_ICON[this.match().place.kind] ?? '🛋️');
}

/**
 * Under a flight: the departure and connection airports that have program lounges, as chips; a chip is highlighted when
 * the visitor's cards open something there. A chip opens the lounge dialog of that airport.
 */
@Component({
  selector: 'app-lounge-hint',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  styles: `
    :host { display: block; }
    .lh { display: flex; flex-wrap: wrap; gap: 0.35rem; align-items: center; margin-top: 0.5rem; font-size: 0.8rem; }
    .lh-label { color: var(--muted); }
    .lh-chip { display: inline-flex; gap: 0.3rem; align-items: center; border: 1px solid var(--border); background: var(--surface); color: var(--text-2);
      border-radius: 999px; padding: 0.18rem 0.6rem; font: inherit; font-size: 0.78rem; cursor: pointer; }
    .lh-chip:hover { border-color: var(--primary); }
    .lh-chip.open { background: var(--primary-soft); border-color: var(--primary); color: var(--primary-deep); font-weight: 700; }
  `,
  template: `
    @if (chips().length) {
      <div class="lh">
        <span class="lh-label">🛋️ {{ 'lounge.hint' | t }}</span>
        @for (c of chips(); track c.iata) {
          <button type="button" class="lh-chip" [class.open]="c.open > 0" (click)="lounges.openDialog(c.iata); $event.stopPropagation()"
                  [title]="(c.open ? 'lounge.chipOpen' : 'lounge.chipAll') | t: { count: c.open || c.all, iata: c.iata }">
            {{ c.iata }} · @if (c.open) { {{ 'lounge.withCards' | t: { count: c.open } }} } @else { {{ c.all }} }
          </button>
        }
      </div>
    }
  `,
})
export class LoungeHintComponent {
  /** Departure and connection airports (IATA), in order. */
  readonly airports = input.required<string[]>();
  protected readonly lounges = inject(LoungeService);

  constructor() {
    void this.lounges.load();
  }

  protected readonly chips = computed(() => {
    this.lounges.data();
    const mine = this.lounges.myCards();
    return [...new Set(this.airports().map((a) => a.toUpperCase()))]
      .map((iata) => {
        const matches = matchPlaces(this.lounges.airport(iata), mine);
        return { iata, all: matches.length, open: matches.filter((m) => m.cards.length).length };
      })
      .filter((c) => c.all > 0);
  });
}

/** The lounge dialog: one airport's places, the visitor's cards (changeable here), and a link to the lounges page. */
@Component({
  selector: 'app-lounge-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, RouterLink, LoungePlaceComponent, IconComponent],
  styles: `
    dialog { width: min(860px, calc(100vw - 24px)); max-height: calc(100dvh - 24px); padding: 0; border: 0; border-radius: var(--radius-lg);
      background: var(--bg); color: var(--text); box-shadow: 0 30px 80px rgba(8, 24, 40, .35); overflow: hidden; }
    dialog[open] { display: flex; flex-direction: column; animation: ld-in 0.18s ease-out; }
    dialog::backdrop { background: rgba(10, 24, 38, .55); backdrop-filter: blur(3px); }
    @keyframes ld-in { from { opacity: 0; transform: translateY(12px) scale(.985); } }
    .ld-head { display: flex; gap: 1rem; align-items: flex-start; padding: 1.1rem 1.3rem; background: linear-gradient(120deg, var(--navy), #1e3a5f); color: #fff; }
    .ld-head h2 { margin: 0; font-size: 1.2rem; color: #fff; }
    .ld-head p { margin: 0.15rem 0 0; opacity: 0.8; font-size: 0.85rem; }
    .ld-close { margin-left: auto; flex: none; width: 2.2rem; height: 2.2rem; border-radius: 999px; border: 1px solid rgba(255,255,255,.3);
      background: transparent; color: #fff; cursor: pointer; display: grid; place-items: center; }
    .ld-body { overflow: auto; padding: 1.1rem 1.3rem 1.4rem; display: grid; gap: 1rem; }
    .ld-cards { display: grid; gap: 0.45rem; padding: 0.8rem 0.9rem; border-radius: var(--radius-md); background: var(--surface); border: 1px solid var(--border); }
    .ld-cards summary { cursor: pointer; font-size: 0.85rem; font-weight: 700; display: flex; flex-wrap: wrap; gap: 0.35rem; align-items: center; }
    .ld-mine { font-weight: 700; font-size: 0.78rem; padding: 0.1rem 0.55rem; border-radius: 999px; background: var(--primary-soft); color: var(--primary-deep); }
    .ld-change { margin-left: auto; font-size: 0.78rem; color: var(--primary-deep); text-decoration: underline; }
    .ld-cards[open] .ld-chips { margin-top: 0.5rem; }
    .ld-chips { display: flex; flex-wrap: wrap; gap: 0.35rem; }
    .ld-chip { border: 1px solid var(--border); background: var(--surface); border-radius: 999px; padding: 0.25rem 0.7rem; font: inherit; font-size: 0.8rem; cursor: pointer; }
    .ld-chip[aria-pressed='true'] { background: var(--primary); border-color: var(--primary); color: #fff; }
    .ld-grid { display: grid; gap: 0.7rem; grid-template-columns: repeat(auto-fill, minmax(min(100%, 250px), 1fr)); }
    .ld-empty { color: var(--muted); }
    .ld-foot { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 0.5rem; font-size: 0.78rem; color: var(--muted); }
  `,
  template: `
    <dialog #dlg (close)="lounges.dialogAirport.set(null)" (click)="backdrop($event)" [attr.aria-labelledby]="'ld-title'">
      @if (airport(); as a) {
        <div class="ld-head">
          <div>
            <h2 id="ld-title">🛋️ {{ 'lounge.dialogTitle' | t: { airport: a.airport } }}</h2>
            <p>{{ a.city }} ({{ a.iata }}) · {{ 'lounge.placesCount' | t: { count: a.places.length } }}</p>
          </div>
          <button type="button" class="ld-close" (click)="close()" [attr.aria-label]="'lounge.close' | t"><app-icon name="x" [size]="18" /></button>
        </div>
        <div class="ld-body">
          <details class="ld-cards" [open]="!lounges.myCards().length">
            <summary>
              {{ 'lounge.pickCards' | t }}
              @for (c of lounges.myCards(); track c.id) { <span class="ld-mine">{{ c.card }}</span> }
              <span class="ld-change">{{ (lounges.myCards().length ? 'lounge.change' : 'lounge.choose') | t }}</span>
            </summary>
            <div class="ld-chips">
              @for (c of lounges.cardsOfCountry(); track c.id) {
                <button type="button" class="ld-chip" [attr.aria-pressed]="lounges.myCardIds().includes(c.id)" (click)="lounges.toggleCard(c.id)">
                  {{ c.bank[i18n.lang()] }} · {{ c.card }}
                </button>
              }
            </div>
          </details>
          @if (matches().length) {
            <div class="ld-grid">
              @for (m of matches(); track m.place.name) { <app-lounge-place [match]="m" [showCards]="lounges.myCards().length > 0" /> }
            </div>
          } @else {
            <p class="ld-empty">{{ a.notes ?? ('lounge.none' | t) }}</p>
          }
          <div class="ld-foot">
            <span>{{ 'lounge.disclaimer' | t: { date: lounges.data()?.checked ?? '' } }}</span>
            <a [routerLink]="i18n.path('lounges')" [queryParams]="{ airport: a.iata }" (click)="close()">{{ 'lounge.toPage' | t }} →</a>
          </div>
        </div>
      }
    </dialog>
  `,
})
export class LoungeDialogComponent {
  protected readonly lounges = inject(LoungeService);
  protected readonly i18n = inject(I18nService);
  private readonly dlg = viewChild<ElementRef<HTMLDialogElement>>('dlg');

  protected readonly airport = computed(() => {
    this.lounges.data();
    return this.lounges.airport(this.lounges.dialogAirport());
  });
  protected readonly matches = computed(() => matchPlaces(this.airport(), this.lounges.myCards()));

  constructor() {
    effect(() => {
      const el = this.dlg()?.nativeElement;
      const open = !!this.lounges.dialogAirport();
      if (!el) return;
      if (open && !el.open) el.showModal();
      if (!open && el.open) el.close();
    });
  }

  protected close(): void {
    this.lounges.dialogAirport.set(null);
  }

  protected backdrop(event: MouseEvent): void {
    if (event.target === this.dlg()?.nativeElement) this.close();
  }
}

