import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LoungeCard, LoungeProgram, LoungeService, matchPlaces } from '../../core/lounges';
import { LoungePlaceComponent, ProgramBadgeComponent } from '../../shared/lounge.components';

const COUNTRY_FLAG: Record<string, string> = { GE: '🇬🇪' };
/** Airports shown as quick picks: Georgian departures first, then the usual connections. */
const QUICK = ['TBS', 'KUT', 'BUS', 'IST', 'DXB', 'DOH', 'VIE', 'MUC', 'WAW', 'DUB', 'BGY', 'MXP'];

/**
 * "Airport lounges": pick where your cards come from, which cards you hold, then an airport — and see the lounges,
 * restaurants and spas your cards open there. Below, every airport where your cards open something, and what the
 * lounge networks are. Data: public/lounges/lounges.json (researched from the banks' and the networks' own pages).
 */
@Component({
  selector: 'app-lounges-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, LoungePlaceComponent, ProgramBadgeComponent],
  styles: `
    .lg { display: grid; gap: 1.4rem; }
    .lg-hero { position: relative; overflow: hidden; border-radius: var(--radius-lg); padding: 2rem 1.6rem; color: #fff;
      background: radial-gradient(120% 140% at 100% 0%, #2c5d8f 0%, transparent 55%), linear-gradient(135deg, #0f2741, #173e63 60%, #0e7c66); }
    .lg-hero h1 { margin: 0.2rem 0 0.4rem; font-size: clamp(1.6rem, 3.4vw, 2.3rem); color: #fff; }
    .lg-hero p { margin: 0; max-width: 46rem; opacity: 0.88; }
    .lg-hero .eyebrow { color: #a7f3d0; }
    .lg-hero-art { position: absolute; right: 1.2rem; bottom: -0.6rem; font-size: 6rem; opacity: 0.18; pointer-events: none; }

    .lg-steps { display: grid; gap: 1rem; grid-template-columns: minmax(0, 1fr); }
    @media (min-width: 1000px) { .lg-steps { grid-template-columns: 0.8fr 2fr 1.3fr; align-items: start; } }
    .lg-step { display: grid; gap: 0.7rem; align-content: start; }
    .lg-step-head { display: flex; gap: 0.6rem; align-items: center; }
    .lg-n { width: 1.9rem; height: 1.9rem; border-radius: 50%; display: grid; place-items: center; font-weight: 800; background: var(--primary); color: #fff; flex: none; }
    .lg-step h2 { margin: 0; font-size: 1.02rem; }
    .lg-country { display: flex; gap: 0.6rem; align-items: center; padding: 0.7rem 0.9rem; border-radius: var(--radius-md);
      border: 2px solid var(--primary); background: var(--primary-soft); font-weight: 700; }
    .lg-country .flag { font-size: 1.6rem; }
    .lg-soon { font-size: 0.78rem; color: var(--muted); }

    .lg-bank { display: grid; gap: 0.4rem; }
    .lg-bank h3 { margin: 0.3rem 0 0; font-size: 0.82rem; color: var(--text-2); text-transform: uppercase; letter-spacing: 0.04em; }
    .lg-cards { display: grid; gap: 0.5rem; grid-template-columns: repeat(auto-fill, minmax(min(100%, 220px), 1fr)); }
    .lg-card { position: relative; display: grid; gap: 0.35rem; text-align: left; padding: 0.75rem 0.85rem 0.8rem; border-radius: 14px; cursor: pointer;
      font: inherit; color: var(--text); background: var(--surface); border: 1.5px solid var(--border); transition: border-color .15s, transform .15s, box-shadow .15s; }
    .lg-card:hover { transform: translateY(-1px); border-color: var(--primary); }
    .lg-card[aria-pressed='true'] { border-color: var(--primary); box-shadow: 0 0 0 3px var(--primary-ring); background: linear-gradient(160deg, var(--surface), var(--primary-soft)); }
    .lg-card .tick { position: absolute; top: 0.55rem; right: 0.6rem; width: 1.3rem; height: 1.3rem; border-radius: 50%; border: 1.5px solid var(--border-strong);
      display: grid; place-items: center; color: #fff; font-size: 0.75rem; }
    .lg-card[aria-pressed='true'] .tick { background: var(--primary); border-color: var(--primary); }
    .lg-card strong { font-size: 0.92rem; padding-right: 1.5rem; }
    .lg-card .net { font-size: 0.72rem; color: var(--muted); }
    .lg-card .badges { display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .lg-card .visits { font-size: 0.74rem; color: var(--text-2); }

    .lg-search { width: 100%; min-height: 2.6rem; padding: 0.5rem 0.8rem; border-radius: var(--radius-md); border: 1.5px solid var(--border-strong);
      background: var(--surface); font: inherit; color: var(--text); }
    .lg-search:focus { outline: none; border-color: var(--primary); box-shadow: 0 0 0 3px var(--primary-ring); }
    .lg-suggest { display: grid; gap: 0.25rem; max-height: 15rem; overflow: auto; }
    .lg-suggest button { display: flex; justify-content: space-between; gap: 0.5rem; text-align: left; border: 1px solid var(--border); background: var(--surface);
      border-radius: var(--radius-sm); padding: 0.4rem 0.6rem; font: inherit; font-size: 0.85rem; cursor: pointer; color: var(--text); }
    .lg-suggest button:hover, .lg-suggest button.on { border-color: var(--primary); background: var(--primary-soft); }
    .lg-suggest small { color: var(--muted); }
    .lg-quick { display: flex; flex-wrap: wrap; gap: 0.3rem; }
    .lg-quick button { border: 1px solid var(--border); background: var(--surface); border-radius: 999px; padding: 0.2rem 0.6rem; font: inherit; font-size: 0.78rem;
      font-weight: 700; cursor: pointer; color: var(--text-2); }
    .lg-quick button.on { background: var(--navy); border-color: var(--navy); color: #fff; }

    .lg-result-head { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 0.5rem; align-items: baseline; }
    .lg-result-head h2 { margin: 0; }
    .lg-grid { display: grid; gap: 0.8rem; grid-template-columns: repeat(auto-fill, minmax(min(100%, 280px), 1fr)); }
    .lg-empty { padding: 1.2rem; border-radius: var(--radius-md); background: var(--surface-tint); color: var(--text-2); }

    .lg-map { display: grid; gap: 0.5rem; grid-template-columns: repeat(auto-fill, minmax(min(100%, 210px), 1fr)); }
    .lg-ap { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 0.6rem; align-items: center; text-align: left; padding: 0.6rem 0.75rem;
      border-radius: var(--radius-md); border: 1px solid var(--border); background: var(--surface); font: inherit; color: var(--text); cursor: pointer; }
    .lg-ap:hover { border-color: var(--primary); }
    .lg-ap .code { font-weight: 800; font-size: 0.95rem; color: var(--navy); }
    .lg-ap .city { font-size: 0.8rem; color: var(--muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lg-ap .count { font-size: 0.75rem; font-weight: 800; padding: 0.1rem 0.5rem; border-radius: 999px; background: var(--surface-tint); }
    .lg-ap .count.open { background: var(--primary); color: #fff; }

    .lg-programs { display: grid; gap: 0.7rem; grid-template-columns: repeat(auto-fill, minmax(min(100%, 260px), 1fr)); }
    .lg-program { display: grid; gap: 0.4rem; padding: 0.9rem 1rem; border-radius: var(--radius-md); background: var(--surface); border: 1px solid var(--border); }
    .lg-program p { margin: 0; font-size: 0.85rem; color: var(--text-2); }
    .lg-note { font-size: 0.8rem; color: var(--muted); }
  `,
  template: `
    <div class="container page lg">
      <section class="lg-hero">
        <p class="eyebrow">🛋️ {{ 'lounge.eyebrow' | t }}</p>
        <h1>{{ 'lounge.title' | t }}</h1>
        <p>{{ 'lounge.lead' | t }}</p>
        <span class="lg-hero-art" aria-hidden="true">✈️</span>
      </section>

      @if (lounges.data(); as d) {
        <section class="lg-steps">
          <div class="card lg-step">
            <div class="lg-step-head"><span class="lg-n">1</span><h2>{{ 'lounge.step1' | t }}</h2></div>
            @for (c of lounges.countries(); track c) {
              <div class="lg-country"><span class="flag" aria-hidden="true">{{ flag(c) }}</span> {{ $any('lounge.country.' + c) | t }}</div>
            }
            <span class="lg-soon">{{ 'lounge.moreCountries' | t }}</span>
          </div>

          <div class="card lg-step">
            <div class="lg-step-head"><span class="lg-n">2</span><h2>{{ 'lounge.step2' | t }}</h2></div>
            @for (b of banks(); track b.name) {
              <div class="lg-bank">
                <h3>{{ b.name }}</h3>
                <div class="lg-cards">
                  @for (c of b.cards; track c.id) {
                    <button type="button" class="lg-card" [attr.aria-pressed]="isMine(c)" (click)="lounges.toggleCard(c.id)">
                      <span class="tick" aria-hidden="true">@if (isMine(c)) { ✓ }</span>
                      <strong>{{ c.card }}</strong>
                      <span class="net">{{ c.network }}</span>
                      <span class="badges">@for (p of c.programs; track p.program) { <app-program-badge [program]="p.program" /> }</span>
                      <span class="visits">{{ visits(c) }}</span>
                    </button>
                  }
                </div>
              </div>
            }
          </div>

          <div class="card lg-step">
            <div class="lg-step-head"><span class="lg-n">3</span><h2>{{ 'lounge.step3' | t }}</h2></div>
            <input class="lg-search" type="search" [value]="query()" (input)="query.set($any($event.target).value)"
                   [placeholder]="'lounge.searchPlaceholder' | t" [attr.aria-label]="'lounge.step3' | t" />
            @if (query().trim()) {
              <div class="lg-suggest">
                @for (a of suggestions(); track a.iata) {
                  <button type="button" [class.on]="a.iata === selected()" (click)="pick(a.iata)">
                    <span><strong>{{ a.iata }}</strong> {{ a.city }}</span><small>{{ 'lounge.placesCount' | t: { count: a.places.length } }}</small>
                  </button>
                } @empty { <span class="lg-note">{{ 'lounge.noAirport' | t }}</span> }
              </div>
            }
            <div class="lg-quick">
              @for (q of quick(); track q) { <button type="button" [class.on]="q === selected()" (click)="pick(q)">{{ q }}</button> }
            </div>
          </div>
        </section>

        @if (selectedAirport(); as a) {
          <section class="lg" aria-labelledby="lg-result">
            <div class="lg-result-head">
              <h2 id="lg-result">{{ a.airport }} ({{ a.iata }})</h2>
              <span class="lg-note">
                @if (lounges.myCards().length) { {{ 'lounge.openCount' | t: { open: openHere(), count: a.places.length } }} }
                @else { {{ 'lounge.placesCount' | t: { count: a.places.length } }} · {{ 'lounge.pickToMatch' | t }} }
              </span>
            </div>
            @if (matches().length) {
              <div class="lg-grid">
                @for (m of matches(); track m.place.name) { <app-lounge-place [match]="m" [showCards]="lounges.myCards().length > 0" /> }
              </div>
            } @else {
              <p class="lg-empty">{{ a.notes ?? ('lounge.none' | t) }}</p>
            }
          </section>
        }

        <section class="lg" aria-labelledby="lg-map">
          <div class="lg-result-head">
            <h2 id="lg-map">{{ (lounges.myCards().length ? 'lounge.mapMine' : 'lounge.mapAll') | t }}</h2>
            <span class="lg-note">{{ 'lounge.mapNote' | t }}</span>
          </div>
          <div class="lg-map">
            @for (r of overview(); track r.iata) {
              <button type="button" class="lg-ap" (click)="pick(r.iata)">
                <span class="code">{{ r.iata }}</span>
                <span class="city">{{ r.city }}</span>
                <span class="count" [class.open]="r.open > 0">@if (lounges.myCards().length) { {{ r.open }}/{{ r.all }} } @else { {{ r.all }} }</span>
              </button>
            }
          </div>
        </section>

        <section class="lg" aria-labelledby="lg-programs">
          <h2 id="lg-programs">{{ 'lounge.programsTitle' | t }}</h2>
          <div class="lg-programs">
            @for (p of d.programs; track p.id) {
              <div class="lg-program">
                <app-program-badge [program]="p.id" />
                <p>{{ p.summary[i18n.lang()] }}</p>
                <a class="lg-note" [href]="p.url" target="_blank" rel="noopener noreferrer nofollow">{{ p.name }} ↗</a>
              </div>
            }
          </div>
          <p class="lg-note">{{ 'lounge.disclaimer' | t: { date: d.checked } }}</p>
        </section>
      } @else {
        <div class="skeleton-list" aria-busy="true"><div class="card skeleton"></div><div class="card skeleton"></div></div>
      }
    </div>
  `,
})
export class LoungesPage {
  /** ?airport= (component input binding): the airport to show. */
  readonly airport = input<string | undefined>();

  protected readonly lounges = inject(LoungeService);
  protected readonly i18n = inject(I18nService);
  private readonly router = inject(Router);

  protected readonly query = signal('');
  protected readonly selected = computed(() => (this.airport() ?? '').toUpperCase() || null);
  protected readonly selectedAirport = computed(() => {
    this.lounges.data();
    return this.lounges.airport(this.selected());
  });
  protected readonly matches = computed(() => matchPlaces(this.selectedAirport(), this.lounges.myCards()));
  protected readonly openHere = computed(() => this.matches().filter((m) => m.cards.length).length);

  protected readonly banks = computed(() => {
    const lang = this.i18n.lang();
    const groups = new Map<string, LoungeCard[]>();
    for (const c of this.lounges.cardsOfCountry()) groups.set(c.bank[lang], [...(groups.get(c.bank[lang]) ?? []), c]);
    return [...groups.entries()].map(([name, cards]) => ({ name, cards }));
  });

  protected readonly suggestions = computed(() => {
    const q = this.query().trim().toLowerCase();
    return (this.lounges.data()?.airports ?? [])
      .filter((a) => a.iata.toLowerCase().startsWith(q) || a.city.toLowerCase().includes(q) || a.airport.toLowerCase().includes(q))
      .slice(0, 8);
  });
  protected readonly quick = computed(() => QUICK.filter((q) => this.lounges.airport(q)));

  /** Every airport with places, those the visitor's cards open most first. */
  protected readonly overview = computed(() => {
    const mine = this.lounges.myCards();
    return (this.lounges.data()?.airports ?? [])
      .filter((a) => a.places.length)
      .map((a) => {
        const m = matchPlaces(a, mine);
        return { iata: a.iata, city: a.city, all: m.length, open: m.filter((x) => x.cards.length).length };
      })
      .sort((a, b) => b.open - a.open || a.iata.localeCompare(b.iata));
  });

  constructor() {
    void this.lounges.load();
    // Scroll the chosen airport into view once its places render.
    effect(() => {
      if (this.selectedAirport()) untracked(() => setTimeout(() => document.getElementById('lg-result')?.scrollIntoView({ behavior: 'smooth', block: 'start' })));
    });
  }

  protected flag(country: string): string {
    return COUNTRY_FLAG[country] ?? '🏳️';
  }

  protected isMine(c: LoungeCard): boolean {
    return this.lounges.myCardIds().includes(c.id);
  }

  protected visits(c: LoungeCard): string {
    const t = (k: Parameters<I18nService['t']>[0], p?: Record<string, string | number>) => this.i18n.t(k, p);
    return c.programs.map((p) => {
      const name = (this.lounges.program(p.program as LoungeProgram)?.name ?? p.program);
      return p.unlimited ? t('lounge.unlimited', { program: name })
        : p.visitsPerYear != null ? t('lounge.visits', { program: name, count: p.visitsPerYear })
        : name;
    }).join(' · ');
  }

  protected pick(iata: string): void {
    this.query.set('');
    void this.router.navigate([], { queryParams: { airport: iata }, queryParamsHandling: 'merge' });
  }
}
