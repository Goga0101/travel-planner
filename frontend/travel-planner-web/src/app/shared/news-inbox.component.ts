import { relativeTime } from '../core/format';
import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { destinationPhoto } from '../core/destination-photos';
import { TranslationKey } from '../core/i18n/en';
import { I18nService } from '../core/i18n/i18n.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { InboxGroup, NewsInboxService } from '../core/news-inbox';
import { NEWS_CATEGORIES, NewsCategory, NewsItem } from '../core/world-news';
import { IconComponent, IconName } from './icon.component';
import { CATEGORY_ICON } from './news-drop.component';

type Filter = 'all' | 'alert' | NewsCategory;

/**
 * The news inbox in the header: a suitcase-mailbox with the number of unread stories. It opens a panel with the news of
 * every city the visitor looked at, grouped by city, alerts pinned on top. A popover, so it sits in the top layer
 * (a bottom sheet on phones) and closes on Escape or a click outside.
 */
@Component({
  selector: 'app-news-inbox',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, RouterLink, TranslatePipe, IconComponent],
  template: `
    <button #btn type="button" class="ni-btn" [class.ni-bump]="bump()" [class.ni-open]="inbox.panelOpen()" (click)="toggle()"
            [attr.aria-label]="'inbox.buttonLabel' | t: { count: inbox.unread() }" aria-haspopup="dialog" [attr.aria-expanded]="inbox.panelOpen()">
      <svg class="ni-icon" viewBox="0 0 32 32" width="24" height="24" fill="none" aria-hidden="true">
        <rect x="4" y="9" width="24" height="18" rx="4" fill="currentColor" fill-opacity="0.12" stroke="currentColor" stroke-width="2.2" />
        <path d="M12 9V6a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v3" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
        <path d="M8 14l8 5 8-5" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
        <rect x="20" y="11" width="4" height="3" rx="0.5" fill="#f59e0b" />
      </svg>
      @if (inbox.unread()) { <span class="ni-badge" aria-hidden="true">{{ inbox.unread() > 9 ? '9+' : inbox.unread() }}</span> }
    </button>

    <div #panel popover="auto" class="ni-panel" role="dialog" [attr.aria-label]="'inbox.title' | t" (toggle)="onToggle($event)">
      <span class="ni-grab" aria-hidden="true"></span>
      <header class="ni-head">
        <div class="ni-head-row">
          <h2>{{ 'inbox.title' | t }}</h2>
          @if (inbox.unread()) { <span class="ni-new">{{ 'inbox.new' | t: { count: inbox.unread() } }}</span> }
          @if (inbox.unread()) { <button type="button" class="ni-markall" (click)="inbox.markAllRead()">{{ 'inbox.markAll' | t }}</button> }
          <button type="button" class="ni-close" (click)="close()" [attr.aria-label]="'tripNews.close' | t"><app-icon name="x" [size]="17" /></button>
        </div>
        @if (inbox.total()) {
          <div class="ni-filters" role="group" [attr.aria-label]="'news.categoryLabel' | t">
            <button type="button" class="ni-chip" [class.active]="filter() === 'all'" [attr.aria-pressed]="filter() === 'all'" (click)="filter.set('all')">{{ 'news.all' | t }}</button>
            @if (inbox.inbox().alerts.length) {
              <button type="button" class="ni-chip ni-chip-alert" [class.active]="filter() === 'alert'" [attr.aria-pressed]="filter() === 'alert'" (click)="filter.set('alert')">
                <span class="ni-dot" aria-hidden="true"></span> {{ 'inbox.alerts' | t }} ({{ inbox.inbox().alerts.length }})
              </button>
            }
            @for (c of presentCategories(); track c) {
              <button type="button" class="ni-chip" [class.active]="filter() === c" [attr.aria-pressed]="filter() === c" (click)="filter.set(c)">{{ category(c) | t }}</button>
            }
          </div>
        }
      </header>

      <div class="ni-list">
        @if (!inbox.total()) {
          <div class="ni-empty">
            <span class="ni-empty-icon"><app-icon name="newspaper" [size]="30" /></span>
            <h3>{{ 'inbox.emptyTitle' | t }}</h3>
            <p>{{ 'inbox.emptyText' | t }}</p>
            <a class="btn btn-primary btn-sm" [routerLink]="i18n.path()" fragment="destinations" (click)="close()">{{ 'news.goDestinations' | t }}</a>
          </div>
        } @else {
          @if (filter() === 'all' || filter() === 'alert') {
            @for (s of inbox.inbox().alerts; track s.id) {
              <ng-container *ngTemplateOutlet="row; context: { $implicit: s, alert: true }" />
            }
          }
          @for (g of visibleGroups(); track g.key) {
            <section class="ni-group">
              <div class="ni-group-head">
                @if (groupPhoto(g); as src) { <img [src]="src" alt="" width="28" height="28" loading="lazy" /> }
                @else { <span class="ni-group-cc">{{ g.country }}</span> }
                <span class="ni-group-name">{{ groupName(g) }}</span>
                <span class="ni-group-count">{{ 'news.results' | t: { count: g.stories.length } }}</span>
                <button type="button" class="ni-forget" (click)="inbox.forget(g.key)" [attr.aria-label]="'inbox.forget' | t: { city: groupName(g) }"><app-icon name="x" [size]="13" /></button>
              </div>
              @for (s of g.stories; track s.id) {
                <ng-container *ngTemplateOutlet="row; context: { $implicit: s, alert: false }" />
              }
            </section>
          }
          @if (filter() !== 'all' && !visibleGroups().length && !(filter() === 'alert' && inbox.inbox().alerts.length)) {
            <p class="ni-none">{{ 'news.noMatchTitle' | t }}</p>
          }
        }
      </div>

      <footer class="ni-foot">
        <span>{{ 'inbox.cities' | t: { count: inbox.inbox().groups.length } }}</span>
        <a class="ni-all" [routerLink]="i18n.path('news')" (click)="close()">{{ 'tripNews.all' | t }} <app-icon name="arrowRight" [size]="13" /></a>
      </footer>
    </div>

    <ng-template #row let-s let-alert="alert">
      @let unread = !inbox.read().has(s.id);
      @let open = expanded() === s.id;
      <article class="ni-row" [class.ni-row-alert]="alert" [class.ni-row-open]="open" [class]="'ni-cat-' + s.category" [attr.id]="'ni-' + s.id">
        <button type="button" class="ni-row-btn" (click)="toggleStory(s)" [attr.aria-expanded]="open">
          <span class="ni-unread" [class.on]="unread" [attr.title]="unread ? ('inbox.unread' | t) : null"></span>
          <span class="ni-row-main">
            <span class="ni-row-meta">
              <span class="ni-tag"><app-icon [name]="icon(s.category)" [size]="12" /> {{ (alert ? 'news.imp.alert' : category(s.category)) | t }}</span>
              <span class="ni-when">{{ relative(s.publishedAt) }}</span>
              @if (alert) { <span class="ni-when">· {{ inbox.countryName(s.country, lang()) }}</span> }
            </span>
            <span class="ni-row-title">{{ s.title[lang()] }}</span>
            @if (!open) { <span class="ni-row-summary">{{ s.summary[lang()] }}</span> }
          </span>
          <app-icon class="ni-chevron" name="arrowRight" [size]="14" />
        </button>
        @if (open) {
          <div class="ni-detail">
            <p>{{ s.summary[lang()] }}</p>
            @if (s.tip) { <p class="ni-tip"><app-icon name="info" [size]="14" /><span><strong>{{ 'news.tip' | t }}</strong> {{ s.tip[lang()] }}</span></p> }
            <p class="ni-source">
              <span>{{ inbox.sourceName(s.sourceId) }}</span>
              <a [href]="s.url" target="_blank" rel="noopener noreferrer nofollow">{{ 'news.readSource' | t }} ↗<span class="visually-hidden"> {{ 'link.newTab' | t }}</span></a>
            </p>
          </div>
        }
      </article>
    </ng-template>
  `,
})
export class NewsInboxComponent {
  protected readonly inbox = inject(NewsInboxService);
  protected readonly i18n = inject(I18nService);
  protected readonly lang = this.i18n.lang;
  protected readonly filter = signal<Filter>('all');
  protected readonly expanded = signal<string | null>(null);
  protected readonly bump = signal(false);
  private readonly btn = viewChild.required<ElementRef<HTMLButtonElement>>('btn');
  private readonly panel = viewChild.required<ElementRef<HTMLElement>>('panel');
  /** A click on the button first light-dismisses the open panel; that same click must not reopen it. */
  private closedAt = 0;

  protected readonly presentCategories = computed(() => {
    const present = new Set(this.inbox.inbox().groups.flatMap((g) => g.stories.map((s) => s.category)));
    return NEWS_CATEGORIES.filter((c) => present.has(c));
  });
  protected readonly visibleGroups = computed<InboxGroup[]>(() => {
    const f = this.filter();
    if (f === 'all') return this.inbox.inbox().groups;
    if (f === 'alert') return [];
    return this.inbox.inbox().groups.map((g) => ({ ...g, stories: g.stories.filter((s) => s.category === f) })).filter((g) => g.stories.length);
  });

  constructor() {
    effect(() => { this.inbox.inboxAnchor = this.btn().nativeElement; });
    // A card landed: the box gives a little hop.
    effect(() => {
      if (!this.inbox.landings()) return;
      untracked(() => {
        this.bump.set(false);
        requestAnimationFrame(() => this.bump.set(true));
        setTimeout(() => this.bump.set(false), 700);
      });
    });
    // Opened from elsewhere (a card's "Read more").
    effect(() => {
      const open = this.inbox.panelOpen();
      untracked(() => (open ? this.show() : this.hide()));
    });
  }

  protected toggle(): void {
    if (this.inbox.panelOpen()) this.inbox.panelOpen.set(false);
    else if (Date.now() - this.closedAt > 300) this.inbox.open(null);
  }

  protected close(): void {
    this.inbox.panelOpen.set(false);
  }

  /** Light dismiss (Escape, click outside) closes the popover without us: keep the state in step. */
  protected onToggle(event: Event): void {
    const open = (event as ToggleEvent).newState === 'open';
    if (!open && this.inbox.panelOpen()) {
      this.closedAt = Date.now();
      this.inbox.panelOpen.set(false);
      this.btn().nativeElement.focus();
    }
  }

  protected toggleStory(s: NewsItem): void {
    const next = this.expanded() === s.id ? null : s.id;
    this.expanded.set(next);
    if (next) this.inbox.markRead(s.id);
  }

  protected groupName(g: InboxGroup): string {
    const lang = this.lang();
    const country = this.inbox.countryName(g.country, lang);
    const city = this.inbox.cityName(g.city, lang);
    return city ? `${city}, ${country}` : country;
  }

  protected groupPhoto(g: InboxGroup): string | null {
    return destinationPhoto(g.city) ?? destinationPhoto(g.stories.flatMap((s) => s.places.map((p) => p.code)).find((c) => destinationPhoto(c)) ?? null);
  }

  protected icon(c: NewsCategory): IconName { return CATEGORY_ICON[c]; }
  protected category(c: NewsCategory): TranslationKey { return `news.cat.${c}` as TranslationKey; }

  protected relative(iso: string): string {
    return relativeTime(iso, this.i18n.lang());
  }

  /** Opens under the button (desktop) or as a bottom sheet (phones, by CSS); expands the requested story. */
  private show(): void {
    const panel = this.panel().nativeElement;
    const rect = this.btn().nativeElement.getBoundingClientRect();
    panel.style.setProperty('--ni-top', `${Math.round(rect.bottom + 10)}px`);
    panel.style.setProperty('--ni-right', `${Math.max(12, Math.round(window.innerWidth - rect.right - 8))}px`);
    const focus = this.inbox.focusStory();
    this.filter.set('all');
    this.expanded.set(focus);
    if (focus) this.inbox.markRead(focus);
    try { if (!panel.matches(':popover-open')) panel.showPopover(); } catch { /* not supported */ }
    if (focus) setTimeout(() => document.getElementById(`ni-${focus}`)?.scrollIntoView({ block: 'nearest' }));
  }

  private hide(): void {
    const panel = this.panel().nativeElement;
    try { if (panel.matches(':popover-open')) panel.hidePopover(); } catch { /* ignore */ }
  }
}
