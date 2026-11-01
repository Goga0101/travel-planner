import { ChangeDetectionStrategy, Component, effect, inject, input, untracked } from '@angular/core';
import { NewsInboxService } from '../core/news-inbox';
import { WorldNewsService } from '../core/world-news';

/**
 * Put it on a page that shows a trip (plan details, flight details). It tells the news inbox which place the visitor is
 * looking at: the place joins "News from my cities" and, the first time in a browser session, its most important
 * unread stories drop in as postcards (shared/news-drop.component.ts). It renders nothing itself.
 */
@Component({
  selector: 'app-trip-news',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
export class TripNewsComponent {
  /** Any one of these is enough: a country code, a catalog city code ("barcelona") or the arrival airport ("BCN"). */
  readonly countryCode = input<string | null | undefined>(null, { alias: 'country' });
  readonly city = input<string | null | undefined>(null);
  readonly airport = input<string | null | undefined>(null);

  private readonly inbox = inject(NewsInboxService);
  private readonly news = inject(WorldNewsService);
  private last: string | null = null;

  constructor() {
    this.news.load();
    effect(() => {
      const file = this.news.file();
      const ref = { country: this.countryCode(), city: this.city(), airport: this.airport() };
      if (!file) return;
      const key = `${ref.country}|${ref.city}|${ref.airport}`;
      untracked(() => {
        if (key === this.last) return;
        this.last = key;
        this.inbox.visit(ref);
      });
    });
  }
}
