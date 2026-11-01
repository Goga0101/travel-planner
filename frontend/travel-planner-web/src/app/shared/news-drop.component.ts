import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { destinationPhoto, destinationSrcset } from '../core/destination-photos';
import { TranslationKey } from '../core/i18n/en';
import { I18nService } from '../core/i18n/i18n.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { DroppedStory, NewsInboxService } from '../core/news-inbox';
import { NewsCategory } from '../core/world-news';
import { IconComponent, IconName } from './icon.component';

export const CATEGORY_ICON: Record<NewsCategory, IconName> = {
  safety: 'alert', transport: 'train', events: 'music', weather: 'cloud', rules: 'scale', tourism: 'landmark',
};

/**
 * News postcards that drop in under the header's top-right corner when a trip opens, stay for a few seconds (paused
 * while pointed at), then fly in an arc into the header's news inbox. Lives once in the app shell.
 *
 * It is a manual popover: the top layer keeps it above everything, including the flight details dialog, and away
 * from the header's backdrop-filter (which would otherwise become the containing block of fixed elements).
 */
@Component({
  selector: 'app-news-drop',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent],
  template: `
    <div #host popover="manual" class="nd" [attr.aria-label]="'tripNews.region' | t">
      <ol class="nd-stack" role="status" aria-live="polite" [class.nd-paused]="paused()"
          (mouseenter)="paused.set(true)" (mouseleave)="paused.set(false)" (focusin)="paused.set(true)" (focusout)="paused.set(false)">
        @for (d of cards(); track d.item.id; let i = $index) {
          @let s = d.item;
          <li class="nd-card" [class.nd-compact]="i > 0" [class.nd-alert]="s.importance === 'alert'" [class]="'nd-cat-' + s.category"
              [attr.data-id]="s.id" [attr.data-flying]="'drop.saving' | t" [style.--nd-delay]="(0.35 + i * 0.55) + 's'">
            <span class="nd-ribbon" aria-hidden="true"></span>
            <span class="nd-stamp" aria-hidden="true">
              @if (s.importance === 'alert') {
                <span class="nd-stamp-code">SAFETY</span>
                <span class="nd-stamp-cc">⚠</span>
                <span class="nd-stamp-post">ALERT</span>
              } @else {
                <span class="nd-stamp-code">{{ d.stamp }}</span>
                <span class="nd-stamp-cc">{{ s.country }}</span>
                <span class="nd-stamp-post">AIR POST</span>
              }
            </span>
            <button type="button" class="nd-x" (click)="send(s.id)" [attr.aria-label]="'drop.toInbox' | t"><app-icon name="x" [size]="15" /></button>
            <div class="nd-photo">
              @if (photo(d); as src) { <img [src]="src" [attr.srcset]="srcset(d)" sizes="64px" alt="" width="64" height="64" decoding="async" /> }
              <span class="nd-photo-code">{{ d.stamp }}</span>
            </div>
            <div class="nd-body">
              <p class="nd-meta">
                <span class="nd-tag"><app-icon [name]="icon(s.category)" [size]="12" /> {{ category(s.category) | t }}</span>
                @if (s.importance === 'alert') { <span class="nd-alert-tag">{{ 'news.imp.alert' | t }}</span> }
                <span class="nd-place">{{ placeName(d) }}</span>
              </p>
              <p class="nd-title">{{ s.title[lang()] }}</p>
              @if (i === 0) {
                <p class="nd-summary">{{ s.summary[lang()] }}</p>
                <div class="nd-actions">
                  <button type="button" class="nd-more" (click)="readMore(s.id)">{{ 'tripNews.read' | t }} <app-icon name="arrowRight" [size]="13" /></button>
                  <!-- The number counts down in CSS (animated custom property), so it pauses with the bar. -->
                  <span class="nd-secs" aria-hidden="true"><app-icon name="clock" [size]="12" /><span class="nd-secs-n"></span> {{ 'drop.seconds' | t }}</span>
                </div>
              }
            </div>
            <span class="nd-timer" (animationend)="timerEnded(s.id, $event)"></span>
          </li>
        }
      </ol>
    </div>
  `,
})
export class NewsDropComponent {
  private readonly inbox = inject(NewsInboxService);
  private readonly i18n = inject(I18nService);
  protected readonly lang = this.i18n.lang;
  protected readonly cards = signal<DroppedStory[]>([]);
  protected readonly paused = signal(false);
  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly flying = new Set<string>();

  constructor() {
    effect(() => {
      const drops = this.inbox.drops();
      if (!drops.length) return;
      untracked(() => {
        this.inbox.drops.set([]);
        this.cards.set(drops);
        this.raise();
      });
    });
  }

  /** The countdown ran out: off to the inbox. */
  protected timerEnded(id: string, event: AnimationEvent): void {
    if (event.animationName.startsWith('nd-countdown')) this.send(id);
  }

  /** "Read more": everything goes to the inbox, which opens on this story. */
  protected readMore(id: string): void {
    this.cards.set([]);
    this.hideIfEmpty();
    this.inbox.landed();
    this.inbox.open(id);
  }

  /** Flies the card along an arc into the header inbox, then removes it. */
  protected send(id: string): void {
    if (this.flying.has(id)) return;
    this.flying.add(id);
    const card = this.host().nativeElement.querySelector<HTMLElement>(`[data-id="${CSS.escape(id)}"]`);
    const target = this.inbox.inboxAnchor;
    const done = () => {
      this.flying.delete(id);
      this.cards.update((list) => list.filter((d) => d.item.id !== id));
      this.inbox.landed();
      this.hideIfEmpty();
    };
    const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!card || !target || !target.getClientRects().length || reduced || !card.animate) {
      if (card?.animate) card.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 200 }).finished.then(done, done);
      else done();
      return;
    }
    const from = card.getBoundingClientRect();
    const to = target.getBoundingClientRect();
    const dx = to.left + to.width / 2 - (from.left + from.width / 2);
    const dy = to.top + to.height / 2 - (from.top + from.height / 2);
    // An arc: first out to the side and up, then down into the box.
    card.style.animation = 'none';
    card.classList.add('nd-flying');
    card.animate(
      [
        { transform: 'translate(0, 0) scale(1) rotate(0deg)', opacity: 1 },
        { transform: `translate(${dx * 0.35 - 60}px, ${dy * 0.55 - 40}px) scale(0.62) rotate(-8deg)`, opacity: 0.9, offset: 0.45 },
        { transform: `translate(${dx}px, ${dy}px) scale(0.08) rotate(-18deg)`, opacity: 0.15 },
      ],
      { duration: 700, easing: 'cubic-bezier(0.45, 0, 0.55, 1)', fill: 'forwards' },
    ).finished.then(done, done);
  }

  protected photo(d: DroppedStory): string | null { return destinationPhoto(this.photoCode(d)); }
  protected srcset(d: DroppedStory): string | null { return destinationSrcset(this.photoCode(d)); }
  protected icon(c: NewsCategory): IconName { return CATEGORY_ICON[c]; }
  protected category(c: NewsCategory): TranslationKey { return `news.cat.${c}` as TranslationKey; }

  protected placeName(d: DroppedStory): string {
    const lang = this.lang();
    return d.item.places[0]?.city[lang] ?? this.inbox.cityName(d.city, lang) ?? this.inbox.countryName(d.item.country, lang);
  }

  private photoCode(d: DroppedStory): string | null {
    return [...d.item.places.map((p) => p.code), d.city].find((c) => c && destinationPhoto(c)) ?? null;
  }

  /** Shows the popover, re-shown so it stacks above a dialog opened after it. */
  private raise(): void {
    const el = this.host().nativeElement;
    // Just under the header as it is now: it wraps to two rows on narrower screens and scrolls away on phones.
    const header = document.querySelector('.topbar')?.getBoundingClientRect();
    el.style.setProperty('--nd-top', `${Math.round(Math.max(header?.bottom ?? 0, 0) + 10)}px`);
    if (!('showPopover' in el)) return;
    try {
      if (el.matches(':popover-open')) el.hidePopover();
      el.showPopover();
    } catch { /* not supported: the stack still renders as a fixed element */ }
  }

  private hideIfEmpty(): void {
    if (this.cards().length) return;
    const el = this.host().nativeElement;
    try { if ('hidePopover' in el && el.matches(':popover-open')) el.hidePopover(); } catch { /* ignore */ }
  }
}
