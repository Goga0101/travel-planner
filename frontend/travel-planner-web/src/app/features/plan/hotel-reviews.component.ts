import { relativeTime } from '../../core/format';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Observable, Subscription, catchError, of, shareReplay } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { HotelReviewDto, HotelReviewsDto } from '../../core/models';
import { SelectedStay, SelectedStayService } from '../../core/selected-stay.service';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe } from '../../shared/pipes';

/**
 * Each Tripadvisor call is paid, so one stay's reviews are fetched once per page session and shared between the tabs
 * that show them. Nothing is stored: reload the page and they are fetched live again.
 */
const sessionRequests = new Map<string, Observable<HotelReviewsDto | null>>();

const TRIP_TYPES = ['COUPLES', 'FAMILY', 'FRIENDS', 'BUSINESS', 'SOLO'] as const;

/**
 * Guest reviews of the hotel the visitor picked: the last two weeks from Tripadvisor, fetched live. While they load a
 * small animated card shows; when Tripadvisor does not know the hotel, has no reviews or no key is configured, nothing
 * is shown at all.
 */
@Component({
  selector: 'app-hotel-reviews',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, LocalDatePipe],
  template: `
    @if (stay(); as st) {
      @switch (state()) {
        @case ('loading') {
          <section class="card hr hr-loading" role="status" [attr.aria-label]="'reviews.loading' | t">
            <div class="hr-loader" aria-hidden="true">
              <span class="hr-loader-bubble"></span><span class="hr-loader-bubble"></span><span class="hr-loader-bubble"></span>
            </div>
            <div>
              <p class="hr-loading-title">{{ 'reviews.loadingTitle' | t: { hotel: st.name } }}</p>
              <p class="small muted">{{ 'reviews.loading' | t }}</p>
            </div>
          </section>
        }
        @case ('ready') {
          @if (data(); as d) {
            <section class="card hr" aria-labelledby="hr-title">
              <header class="hr-head">
                <div class="hr-head-main">
                  <p class="eyebrow"><app-icon name="users" [size]="14" /> {{ 'reviews.eyebrow' | t }}</p>
                  <h2 id="hr-title">{{ d.hotelName ?? st.name }}</h2>
                  <p class="small muted">
                    @if (d.recent) {
                      {{ 'reviews.windowLine' | t: { days: d.windowDays, count: d.reviews.length } }}
                    } @else {
                      {{ 'reviews.latestLine' | t: { count: d.reviews.length } }}
                    }
                  </p>
                </div>
                @if (d.averageRating; as avg) {
                  <div class="hr-score" [attr.aria-label]="'reviews.scoreLabel' | t: { score: avg }">
                    <span class="hr-score-num">{{ avg.toFixed(1) }}</span>
                    <span class="hr-bubbles hr-bubbles-lg" aria-hidden="true">
                      @for (b of [1, 2, 3, 4, 5]; track b) { <span class="hr-bubble" [class.full]="avg >= b - 0.25" [class.half]="avg < b - 0.25 && avg >= b - 0.75"></span> }
                    </span>
                    <span class="hr-score-of">{{ 'reviews.outOf5' | t }}</span>
                  </div>
                }
              </header>

              @if (!d.recent) {
                <p class="hr-note"><app-icon name="info" [size]="16" /> {{ 'reviews.noneRecent' | t: { days: d.windowDays } }}</p>
              }

              <div class="hr-rail-wrap">
                <ul class="hr-rail" #rail tabindex="0" [attr.aria-label]="'reviews.listLabel' | t">
                  @for (r of d.reviews; track r.id) {
                    <li class="hr-card">
                      <div class="hr-author">
                        @if (r.avatarUrl) {
                          <img class="hr-avatar" [src]="r.avatarUrl" alt="" width="40" height="40" loading="lazy" referrerpolicy="no-referrer" />
                        } @else {
                          <span class="hr-avatar hr-initials" [style.background]="colour(r.author)" aria-hidden="true">{{ initials(r.author) }}</span>
                        }
                        <span class="hr-who">
                          <strong>{{ r.author ?? ('reviews.guest' | t) }}</strong>
                          @if (r.authorLocation) { <span class="small muted">{{ r.authorLocation }}</span> }
                        </span>
                      </div>
                      <div class="hr-rating-row">
                        <span class="hr-bubbles" [attr.aria-label]="'reviews.scoreLabel' | t: { score: r.rating }">
                          @for (b of [1, 2, 3, 4, 5]; track b) { <span class="hr-bubble" [class.full]="r.rating >= b"></span> }
                        </span>
                        <time class="small muted" [attr.datetime]="r.publishedAt" [title]="r.publishedAt | ldate: 'd MMMM y'">{{ relative(r.publishedAt) }}</time>
                      </div>
                      @if (tripType(r); as tt) { <span class="hr-trip">{{ tt | t }}</span> }
                      @if (r.title) { <h3 class="hr-title">{{ r.title }}</h3> }
                      <p class="hr-text" [class.open]="expanded().has(r.id)">{{ r.text }}</p>
                      @if (r.text.length > 260) {
                        <button type="button" class="text-link small hr-more" (click)="toggle(r.id)" [attr.aria-expanded]="expanded().has(r.id)">
                          {{ (expanded().has(r.id) ? 'reviews.less' : 'reviews.more') | t }}
                        </button>
                      }
                      @if (r.ownerResponse) {
                        <details class="hr-owner">
                          <summary>{{ 'reviews.ownerReply' | t }}</summary>
                          <p>{{ r.ownerResponse }}</p>
                        </details>
                      }
                      @if (r.url) {
                        <a class="hr-link small" [href]="r.url" target="_blank" rel="noopener noreferrer nofollow">
                          {{ 'reviews.onTripadvisor' | t }} <span aria-hidden="true">↗</span><span class="visually-hidden"> {{ 'link.newTab' | t }}</span>
                        </a>
                      }
                    </li>
                  }
                </ul>
                @if (d.reviews.length > 1) {
                  <button type="button" class="hr-nav hr-prev" (click)="scroll(-1)" [attr.aria-label]="'reviews.prev' | t"><app-icon name="arrowLeft" [size]="18" /></button>
                  <button type="button" class="hr-nav hr-next" (click)="scroll(1)" [attr.aria-label]="'reviews.next' | t"><app-icon name="arrowRight" [size]="18" /></button>
                }
              </div>

              <footer class="hr-foot">
                <span class="hr-attr"><span class="hr-ta-dot" aria-hidden="true"></span> {{ 'reviews.attribution' | t }}</span>
                @if (lang() !== 'en') { <span class="small muted">{{ 'reviews.originalLanguage' | t }}</span> }
                @if (d.hotelUrl) {
                  <a class="small" [href]="d.hotelUrl" target="_blank" rel="noopener noreferrer nofollow">{{ 'reviews.allOnTripadvisor' | t }} ↗</a>
                }
              </footer>
            </section>
          }
        }
      }
    }
  `,
})
export class HotelReviewsComponent {
  readonly planId = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly stays = inject(SelectedStayService);
  private readonly i18n = inject(I18nService);
  protected readonly lang = this.i18n.lang;

  protected readonly stay = computed<SelectedStay | null>(() => {
    this.stays.all();
    return this.stays.get(this.planId());
  });
  /** 'hidden' covers every case with nothing to show: no key, unknown hotel, no reviews, an error. */
  protected readonly state = signal<'loading' | 'ready' | 'hidden'>('hidden');
  protected readonly data = signal<HotelReviewsDto | null>(null);
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());
  private readonly rail = viewChild<ElementRef<HTMLUListElement>>('rail');
  private request?: Subscription;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
    effect(() => {
      const planId = this.planId();
      const stay = this.stay();
      untracked(() => this.load(planId, stay));
    });
  }

  private load(planId: string, stay: SelectedStay | null): void {
    this.request?.unsubscribe();
    this.data.set(null);
    this.expanded.set(new Set());
    if (!stay) { this.state.set('hidden'); return; }
    const key = `${planId}|${stay.latitude.toFixed(5)}|${stay.longitude.toFixed(5)}|${stay.name}`;
    let request = sessionRequests.get(key);
    if (!request) {
      request = this.api.getHotelReviews(planId, stay.name, stay.latitude, stay.longitude).pipe(
        catchError(() => { sessionRequests.delete(key); return of(null); }),
        shareReplay(1),
      );
      sessionRequests.set(key, request);
    }
    this.state.set('loading');
    this.request = request.subscribe((d) => {
      const usable = d && d.matched && d.reviews.length > 0;
      this.data.set(usable ? d : null);
      this.state.set(usable ? 'ready' : 'hidden');
    });
  }

  protected toggle(id: string): void {
    this.expanded.update((s) => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n; });
  }

  protected scroll(direction: 1 | -1): void {
    const el = this.rail()?.nativeElement;
    if (!el) return;
    const card = el.querySelector<HTMLElement>('.hr-card');
    el.scrollBy({ left: direction * ((card?.offsetWidth ?? 300) + 16), behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
  }

  protected tripType(r: HotelReviewDto): TranslationKey | null {
    const t = (r.tripType ?? '').toUpperCase();
    return (TRIP_TYPES as readonly string[]).includes(t) ? (`reviews.trip.${t}` as TranslationKey) : null;
  }

  protected initials(name: string | null | undefined): string {
    const parts = (name ?? '?').trim().split(/\s+/).filter(Boolean);
    return ((parts[0]?.[0] ?? '?') + (parts[1]?.[0] ?? '')).toUpperCase();
  }

  /** A stable pastel colour per reviewer for the initials avatar. */
  protected colour(name: string | null | undefined): string {
    let h = 0;
    for (const ch of name ?? '') h = (h * 31 + ch.charCodeAt(0)) % 360;
    return `hsl(${h} 55% 45%)`;
  }

  protected relative(iso: string): string {
    return relativeTime(iso, this.i18n.lang());
  }
}
