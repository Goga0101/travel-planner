import { relativeTime } from '../../core/format';
import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { destinationPhoto, destinationSrcset } from '../../core/destination-photos';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import {
  DayGroup, NEWS_CATEGORIES, NEWS_REGIONS, NewsCategory, NewsItem, NewsPlace, NewsRegion, NewsSort, WorldNewsService,
  countBy, dayGroup, filterNews, liveItems, sortNews,
} from '../../core/world-news';
import { IconComponent, IconName } from '../../shared/icon.component';
import { LocalDatePipe } from '../../shared/pipes';

const CATEGORY_ICON: Record<NewsCategory, IconName> = {
  safety: 'alert', transport: 'plane', events: 'music', weather: 'cloud', rules: 'scale', tourism: 'landmark',
};
const DAY_GROUPS: readonly DayGroup[] = ['today', 'yesterday', 'week', 'older'];

/**
 * World news for travellers: safety, transport, events, weather, entry rules and tourism news for the destination
 * countries, from each country's public broadcaster, summarised in Georgian and English by the world-news-editor agent.
 * Static data (public/news/world-news.json), so it works while the API is offline. Filters live in the query string,
 * so a filtered view can be shared.
 */
@Component({
  selector: 'app-world-news-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, RouterLink, TranslatePipe, IconComponent, LocalDatePipe],
  template: `
    <div class="container page news-page">
      <header class="news-hero">
        <div>
          <p class="eyebrow"><app-icon name="newspaper" [size]="14" /> {{ 'news.eyebrow' | t }}</p>
          <h1 class="page-title">{{ 'news.title' | t }}</h1>
          <p class="lead">{{ 'news.lead' | t }}</p>
        </div>
        @if (news.file(); as f) {
          <dl class="news-stats">
            <div><dt>{{ 'news.statStories' | t }}</dt><dd>{{ live().length }}</dd></div>
            <div><dt>{{ 'news.statCountries' | t }}</dt><dd>{{ countryCounts().size }}</dd></div>
            <div><dt>{{ 'news.statUpdated' | t }}</dt><dd><time [attr.datetime]="f.generatedAt">{{ f.generatedAt | ldate: 'd MMM, HH:mm' }}</time></dd></div>
          </dl>
        }
      </header>

      @switch (news.state()) {
        @case ('error') {
          <div class="card empty-state" role="alert">
            <span class="icon-badge icon-badge-lg"><app-icon name="alert" [size]="28" /></span>
            <h2>{{ 'news.errorTitle' | t }}</h2>
            <p class="muted">{{ 'news.errorText' | t }}</p>
            <button type="button" class="btn btn-primary" (click)="retry()">{{ 'news.retry' | t }}</button>
          </div>
        }
        @case ('ready') {
          @if (!live().length) {
            <div class="card empty-state">
              <span class="icon-badge icon-badge-lg"><app-icon name="newspaper" [size]="28" /></span>
              <h2>{{ 'news.emptyTitle' | t }}</h2>
              <p class="muted">{{ 'news.emptyText' | t }}</p>
              <a class="btn btn-primary" [routerLink]="i18n.path()">{{ 'news.goPlan' | t }} <app-icon name="arrowRight" [size]="16" /></a>
            </div>
          } @else {
            @if (alerts().length) {
              <section class="news-alerts" aria-labelledby="news-alerts-title">
                <h2 id="news-alerts-title"><app-icon name="alert" [size]="18" /> {{ 'news.alertsTitle' | t }}</h2>
                <ul>
                  @for (a of alerts(); track a.id) {
                    <li>
                      <button type="button" class="alert-link" (click)="goToStory(a)" [attr.aria-label]="('news.goToStory' | t) + ': ' + a.title[lang()]">
                        <span class="cc">{{ a.country }}</span>
                        <strong>{{ a.title[lang()] }}</strong>
                        <span class="when">{{ relative(a.publishedAt) }}</span>
                      </button>
                    </li>
                  }
                </ul>
              </section>
            }

            <div class="news-layout">
              <div class="news-main">
                <div class="card news-toolbar" role="search">
                  <div class="toolbar-row">
                    <label class="news-search">
                      <span class="visually-hidden">{{ 'news.searchLabel' | t }}</span>
                      <app-icon name="search" [size]="18" />
                      <input type="search" [value]="query()" (input)="setQuery($any($event.target).value)" [placeholder]="'news.searchPlaceholder' | t" />
                    </label>
                    <label class="news-select">
                      <span class="visually-hidden">{{ 'news.countryLabel' | t }}</span>
                      <select [value]="country() ?? ''" (change)="setCountry($any($event.target).value || null)">
                        <option value="">{{ 'news.allCountries' | t }}</option>
                        @for (c of countryOptions(); track c.code) {
                          <option [value]="c.code" [selected]="c.code === country()">{{ c.name }} ({{ c.count }})</option>
                        }
                      </select>
                    </label>
                    <div class="segmented" role="group" [attr.aria-label]="'news.sortLabel' | t">
                      @for (s of sorts; track s) {
                        <button type="button" [class.active]="sort() === s" [attr.aria-pressed]="sort() === s" (click)="setSort(s)">{{ sortLabel(s) | t }}</button>
                      }
                    </div>
                  </div>
                  <div class="chip-row" role="group" [attr.aria-label]="'news.categoryLabel' | t">
                    <button type="button" class="filter-chip" [class.active]="!category()" [attr.aria-pressed]="!category()" (click)="setCategory(null)">
                      {{ 'news.all' | t }} <span class="chip-count">{{ scoped().length }}</span>
                    </button>
                    @for (c of categories; track c) {
                      @if (categoryCounts().get(c); as n) {
                        <button type="button" class="filter-chip" [class.active]="category() === c" [attr.aria-pressed]="category() === c" (click)="setCategory(c)">
                          <app-icon [name]="categoryIcon[c]" [size]="15" /> {{ categoryLabel(c) | t }} <span class="chip-count">{{ n }}</span>
                        </button>
                      }
                    }
                  </div>
                  <div class="chip-row" role="group" [attr.aria-label]="'news.regionLabel' | t">
                    <button type="button" class="filter-chip" [class.active]="!region()" [attr.aria-pressed]="!region()" (click)="setRegion(null)">
                      <app-icon name="globe" [size]="15" /> {{ 'news.allRegions' | t }}
                    </button>
                    @for (r of regions; track r) {
                      @if (regionCounts().get(r); as n) {
                        <button type="button" class="filter-chip" [class.active]="region() === r" [attr.aria-pressed]="region() === r" (click)="setRegion(r)">
                          {{ regionLabel(r) | t }} <span class="chip-count">{{ n }}</span>
                        </button>
                      }
                    }
                  </div>
                  <div class="toolbar-status">
                    <span aria-live="polite">{{ 'news.results' | t: { count: shown().length } }}</span>
                    @if (hasFilters()) {
                      <button type="button" class="text-link" (click)="reset()"><app-icon name="x" [size]="15" /> {{ 'news.reset' | t }}</button>
                    }
                  </div>
                </div>

                @if (!shown().length) {
                  <div class="card empty-state">
                    <span class="icon-badge icon-badge-lg"><app-icon name="search" [size]="28" /></span>
                    <h2>{{ 'news.noMatchTitle' | t }}</h2>
                    <p class="muted">{{ 'news.noMatchText' | t }}</p>
                    <button type="button" class="btn btn-secondary" (click)="reset()">{{ 'news.reset' | t }}</button>
                  </div>
                } @else if (sort() === 'newest') {
                  @for (g of grouped(); track g.group) {
                    <h2 class="news-day">{{ dayLabel(g.group) | t }}</h2>
                    <ul class="story-list" style="margin-top: 0">
                      @for (i of g.items; track i.id) { <li><ng-container *ngTemplateOutlet="story; context: { $implicit: i }" /></li> }
                    </ul>
                  }
                } @else {
                  <ul class="story-list">
                    @for (i of shown(); track i.id) { <li><ng-container *ngTemplateOutlet="story; context: { $implicit: i }" /></li> }
                  </ul>
                }
              </div>

              <aside class="news-aside" [attr.aria-label]="'news.asideLabel' | t">
                <nav class="card" [attr.aria-label]="'news.continueTitle' | t">
                  <h2 class="aside-title"><app-icon name="compass" [size]="18" /> {{ 'news.continueTitle' | t }}</h2>
                  <ul class="go-list">
                    <li><a [routerLink]="i18n.path()"><span class="go-icon"><app-icon name="wallet" [size]="17" /></span>{{ 'news.goPlan' | t }}<app-icon class="go-arrow" name="arrowRight" [size]="15" /></a></li>
                    <li><a [routerLink]="i18n.path()" fragment="destinations"><span class="go-icon"><app-icon name="pin" [size]="17" /></span>{{ 'news.goDestinations' | t }}<app-icon class="go-arrow" name="arrowRight" [size]="15" /></a></li>
                    <li><a [routerLink]="i18n.path('flights')"><span class="go-icon"><app-icon name="calendar" [size]="17" /></span>{{ 'news.goFlights' | t }}<app-icon class="go-arrow" name="arrowRight" [size]="15" /></a></li>
                    <li><a [routerLink]="i18n.path('transit')"><span class="go-icon"><app-icon name="bus" [size]="17" /></span>{{ 'news.goTransit' | t }}<app-icon class="go-arrow" name="arrowRight" [size]="15" /></a></li>
                    <li><a [routerLink]="i18n.path('saved')"><span class="go-icon"><app-icon name="heart" [size]="17" /></span>{{ 'news.goSaved' | t }}<app-icon class="go-arrow" name="arrowRight" [size]="15" /></a></li>
                  </ul>
                </nav>

                <section class="card" aria-labelledby="news-countries-title">
                  <h2 class="aside-title" id="news-countries-title"><app-icon name="globe" [size]="18" /> {{ 'news.countriesTitle' | t }}</h2>
                  @for (r of countriesByRegion(); track r.region) {
                    <p class="aside-region">{{ regionLabel(r.region) | t }}</p>
                    <ul class="country-list">
                      @for (c of r.countries; track c.code) {
                        <li>
                          <button type="button" class="country-btn" [class.active]="country() === c.code" [attr.aria-pressed]="country() === c.code" (click)="toggleCountry(c.code)">
                            <span class="cc">{{ c.code }}</span> {{ c.name }}
                            @if (c.alert) { <span class="alert-dot" [title]="'news.imp.alert' | t"></span> }
                            <span class="count">{{ c.count }}</span>
                          </button>
                        </li>
                      }
                    </ul>
                  }
                </section>

                <section class="card" aria-labelledby="news-about-title">
                  <h2 class="aside-title" id="news-about-title"><app-icon name="info" [size]="18" /> {{ 'news.aboutTitle' | t }}</h2>
                  <p class="small muted" style="margin: 0">{{ 'news.aboutText' | t }}</p>
                  @if (news.file(); as f) {
                    <details>
                      <summary class="small">{{ 'news.sourcesUsed' | t }} ({{ sourceList().length }})</summary>
                      <ul class="source-list">
                        @for (s of sourceList(); track s.id) {
                          <li><a [href]="s.url" target="_blank" rel="noopener noreferrer">{{ s.name }}</a></li>
                        }
                      </ul>
                    </details>
                  }
                </section>
              </aside>
            </div>
          }
        }
        @default {
          <div class="news-skeleton" role="status" [attr.aria-label]="'news.loading' | t"><div></div><div></div><div></div></div>
        }
      }
    </div>

    <ng-template #story let-i>
      <article class="story" [class]="'cat-' + i.category" [class.has-photo]="!!photo(i)" [class.flash]="flashed() === i.id" [attr.id]="'story-' + i.id" tabindex="-1">
        <div class="story-body">
          <div class="story-top">
            <span class="cc">{{ i.country }}</span>
            <span class="story-place">{{ countryName(i.country) }}@for (p of i.places; track p.code) {<span> · {{ p.city[lang()] }}</span>}</span>
            <span class="story-cat"><app-icon [name]="iconFor(i.category)" [size]="14" /> {{ categoryLabel(i.category) | t }}</span>
            @if (i.importance !== 'info') {
              <span class="story-imp" [class]="'imp-' + i.importance">{{ importanceLabel(i.importance) | t }}</span>
            }
          </div>
          <h3 class="story-title">{{ i.title[lang()] }}</h3>
          <p class="story-summary">{{ i.summary[lang()] }}</p>
          @if (i.tip) {
            <p class="story-tip"><app-icon name="info" [size]="16" /><span><strong>{{ 'news.tip' | t }}</strong> {{ i.tip[lang()] }}</span></p>
          }
          <div class="story-foot">
            <span class="story-source">
              <strong>{{ sourceName(i.sourceId) }}</strong> ·
              <time [attr.datetime]="i.publishedAt" [title]="i.publishedAt | ldate: 'd MMMM y, HH:mm'">{{ relative(i.publishedAt) }}</time> ·
              {{ 'news.original' | t: { language: languageName(i.originalLanguage) } }}
            </span>
            <span class="story-actions">
              @if (flightPlace(i); as p) {
                <a class="btn btn-ghost btn-sm" [routerLink]="i18n.path('flights')" [queryParams]="flightParams(p)">
                  <app-icon name="plane" [size]="15" /> {{ 'news.flightsTo' | t: { city: p.city[lang()] } }}
                </a>
              }
              <a class="btn btn-secondary btn-sm" [href]="i.url" target="_blank" rel="noopener noreferrer nofollow">
                {{ 'news.readSource' | t }} <span aria-hidden="true">↗</span><span class="visually-hidden"> {{ 'link.newTab' | t }}</span>
              </a>
            </span>
          </div>
        </div>
        @if (photo(i); as src) {
          <img class="story-photo" [src]="src" [attr.srcset]="srcset(i)" sizes="190px" alt="" loading="lazy" decoding="async" width="480" height="320" />
        }
      </article>
    </ng-template>
  `,
})
export class WorldNewsPage {
  protected readonly news = inject(WorldNewsService);
  protected readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly categories = NEWS_CATEGORIES;
  protected readonly regions = NEWS_REGIONS;
  protected readonly sorts: readonly NewsSort[] = ['important', 'newest'];
  protected readonly categoryIcon = CATEGORY_ICON;
  protected readonly lang = this.i18n.lang;

  // Filters, initialised from the query string so filtered views can be shared.
  private readonly q = this.route.snapshot.queryParamMap;
  protected readonly category = signal<NewsCategory | null>(pick(this.q.get('cat'), NEWS_CATEGORIES));
  protected readonly region = signal<NewsRegion | null>(pick(this.q.get('region'), NEWS_REGIONS));
  protected readonly country = signal<string | null>(/^[A-Z]{2}$/.test(this.q.get('country') ?? '') ? this.q.get('country') : null);
  protected readonly sort = signal<NewsSort>(this.q.get('sort') === 'newest' ? 'newest' : 'important');
  protected readonly query = signal(this.q.get('q') ?? '');
  protected readonly flashed = signal<string | null>(null);

  protected readonly live = computed(() => { const f = this.news.file(); return f ? liveItems(f) : []; });
  protected readonly alerts = computed(() => sortNews(this.live().filter((i) => i.importance === 'alert'), 'newest').slice(0, 5));

  /** Everything but the category filter: the category chips count within this set. */
  protected readonly scoped = computed(() => {
    const f = this.news.file();
    return f ? filterNews(this.live(), f, { category: null, region: this.region(), country: this.country(), query: this.query() }) : [];
  });
  protected readonly shown = computed(() => sortNews(this.category() ? this.scoped().filter((i) => i.category === this.category()) : this.scoped(), this.sort()));
  protected readonly grouped = computed(() => {
    const now = new Date();
    return DAY_GROUPS.map((group) => ({ group, items: this.shown().filter((i) => dayGroup(i.publishedAt, now) === group) })).filter((g) => g.items.length);
  });

  protected readonly categoryCounts = computed(() => countBy(this.scoped(), (i) => i.category));
  protected readonly regionCounts = computed(() => countBy(this.live(), (i) => this.news.file()?.countries[i.country]?.region));
  protected readonly countryCounts = computed(() => countBy(this.live(), (i) => i.country));
  protected readonly countryOptions = computed(() =>
    [...this.countryCounts()].map(([code, count]) => ({ code, count, name: this.countryName(code) }))
      .sort((a, b) => a.name.localeCompare(b.name, this.i18n.intlLocale())),
  );
  protected readonly countriesByRegion = computed(() => {
    const f = this.news.file();
    if (!f) return [];
    const alerting = new Set(this.live().filter((i) => i.importance === 'alert').map((i) => i.country));
    return NEWS_REGIONS.map((region) => ({
      region,
      countries: this.countryOptions().filter((c) => f.countries[c.code]?.region === region).map((c) => ({ ...c, alert: alerting.has(c.code) })),
    })).filter((r) => r.countries.length);
  });
  protected readonly sourceList = computed(() => {
    const f = this.news.file();
    return f ? Object.entries(f.sources).map(([id, s]) => ({ id, ...s })).sort((a, b) => a.name.localeCompare(b.name)) : [];
  });
  protected readonly hasFilters = computed(() => !!(this.category() || this.region() || this.country() || this.query().trim()));

  constructor() {
    this.news.load();
  }

  protected retry(): void {
    this.news.state.set('idle');
    this.news.load();
  }

  protected setQuery(value: string): void { this.query.set(value); this.syncUrl(); }
  protected setCategory(value: NewsCategory | null): void { this.category.set(value); this.syncUrl(); }
  protected setRegion(value: NewsRegion | null): void {
    this.region.set(value);
    // A country outside the new region would hide everything.
    const f = this.news.file();
    if (value && this.country() && f?.countries[this.country()!]?.region !== value) this.country.set(null);
    this.syncUrl();
  }
  protected setCountry(value: string | null): void {
    this.country.set(value);
    if (value) this.region.set(null);
    this.syncUrl();
  }
  protected toggleCountry(code: string): void { this.setCountry(this.country() === code ? null : code); }
  protected setSort(value: NewsSort): void { this.sort.set(value); this.syncUrl(); }
  protected reset(): void {
    this.category.set(null); this.region.set(null); this.country.set(null); this.query.set('');
    this.syncUrl();
  }

  /** Alerts at the top jump to their full story, clearing filters that would hide it. */
  protected goToStory(item: NewsItem): void {
    if (!this.shown().some((i) => i.id === item.id)) this.reset();
    this.flashed.set(null);
    setTimeout(() => {
      const el = document.getElementById(`story-${item.id}`);
      el?.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block: 'start' });
      el?.focus({ preventScroll: true });
      this.flashed.set(item.id);
    });
  }

  protected countryName(code: string): string { return this.news.file()?.countries[code]?.name[this.lang()] ?? code; }
  protected sourceName(id: string): string { return this.news.file()?.sources[id]?.name ?? id; }
  protected photo(i: NewsItem): string | null { return destinationPhoto(i.places[0]?.code); }
  protected srcset(i: NewsItem): string | null { return destinationSrcset(i.places[0]?.code); }
  protected flightPlace(i: NewsItem): NewsPlace | null { return i.places.find((p) => p.iata) ?? null; }
  protected flightParams(p: NewsPlace): Record<string, string> {
    return { from: 'TBS', fromName: this.i18n.t('news.fromTbilisi'), to: p.iata!, toName: `${p.city[this.lang()]} (${p.iata})` };
  }

  protected iconFor(c: NewsCategory): IconName { return CATEGORY_ICON[c]; }
  protected categoryLabel(c: NewsCategory): TranslationKey { return `news.cat.${c}` as TranslationKey; }
  protected regionLabel(r: NewsRegion): TranslationKey { return `news.region.${r}` as TranslationKey; }
  protected sortLabel(s: NewsSort): TranslationKey { return `news.sort.${s}` as TranslationKey; }
  protected dayLabel(g: DayGroup): TranslationKey { return `news.day.${g}` as TranslationKey; }
  protected importanceLabel(i: 'alert' | 'notice'): TranslationKey { return `news.imp.${i}` as TranslationKey; }

  protected relative(iso: string): string {
    return relativeTime(iso, this.i18n.lang());
  }

  protected languageName(code: string): string {
    try {
      return new Intl.DisplayNames([this.i18n.intlLocale()], { type: 'language' }).of(code) ?? code;
    } catch {
      return code;
    }
  }

  private syncUrl(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: {
        cat: this.category(), region: this.region(), country: this.country(),
        sort: this.sort() === 'newest' ? 'newest' : null, q: this.query().trim() || null,
      },
    });
  }
}

function pick<T extends string>(value: string | null, allowed: readonly T[]): T | null {
  return allowed.includes(value as T) ? (value as T) : null;
}
