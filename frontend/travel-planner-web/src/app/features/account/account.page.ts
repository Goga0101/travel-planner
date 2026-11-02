import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { tripDays, tripProgress } from '../../core/booked-trips';
import { destinationPhoto, destinationSrcset } from '../../core/destination-photos';
import { toIsoDate } from '../../core/format';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { SavedTripsService } from '../../core/local-lists';
import { BookedTripDto, LeaderboardEntryDto } from '../../core/models';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe, MoneyPipe } from '../../shared/pipes';

type TripFilter = 'all' | 'Draft' | 'Booked' | 'Done';
type Tab = 'trips' | 'favorites';

/** The state a trip card shows: drafts, bought trips, and bought trips with every day ticked. */
function stateOf(t: BookedTripDto): Exclude<TripFilter, 'all'> {
  return t.completedAtUtc ? 'Done' : t.status;
}

/**
 * "My account": the profile, the trips saved to the account (draft → bought → completed, with progress), the plans
 * favourited with the heart (kept in this browser), and the account actions. Signed out it offers the sign-in.
 */
@Component({
  selector: 'app-account-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, IconComponent, LocalDatePipe, MoneyPipe],
  styles: `
    .ac { display: grid; gap: 1.25rem; }
    .ac-profile { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 1.1rem; align-items: center; }
    @media (min-width: 900px) { .ac-profile { grid-template-columns: auto minmax(0, 1fr) auto; } }
    .ac-avatar { width: 4.5rem; height: 4.5rem; border-radius: 50%; object-fit: cover; display: grid; place-items: center; font-size: 1.6rem; font-weight: 800;
      background: var(--primary-soft); color: var(--primary-deep); border: 3px solid var(--surface); box-shadow: var(--shadow); }
    .ac-name { margin: 0; font-size: 1.45rem; }
    .ac-sub { margin: 0.15rem 0 0; color: var(--muted); font-size: 0.88rem; overflow-wrap: anywhere; }
    .ac-stats { grid-column: 1 / -1; display: grid; grid-template-columns: repeat(auto-fit, minmax(7.5rem, 1fr)); gap: 0.5rem; }
    @media (min-width: 900px) { .ac-stats { grid-column: auto; grid-template-columns: repeat(4, 7.5rem); } }
    .ac-stat { padding: 0.65rem 0.8rem; border-radius: var(--radius-md); background: var(--surface-tint); border: 1px solid var(--border); }
    .ac-stat strong { display: block; font-size: 1.35rem; line-height: 1.1; }
    .ac-stat span { font-size: 0.75rem; color: var(--muted); }

    .ac-tabs { display: flex; gap: 0.4rem; flex-wrap: wrap; border-bottom: 1px solid var(--border); }
    .ac-tab { border: 0; background: none; font: inherit; font-weight: 700; padding: 0.6rem 0.9rem; color: var(--muted); cursor: pointer;
      border-bottom: 3px solid transparent; margin-bottom: -1px; display: inline-flex; gap: 0.4rem; align-items: center; }
    .ac-tab[aria-selected='true'] { color: var(--primary-deep); border-bottom-color: var(--primary); }
    .ac-tab .count { font-size: 0.72rem; padding: 0.05rem 0.45rem; border-radius: 999px; background: var(--surface-tint); color: var(--text-2); }

    .ac-filters { display: flex; gap: 0.4rem; flex-wrap: wrap; }
    .ac-chip { border: 1px solid var(--border); background: var(--surface); border-radius: 999px; padding: 0.35rem 0.85rem; font: inherit; font-size: 0.85rem;
      cursor: pointer; color: var(--text-2); }
    .ac-chip[aria-pressed='true'] { background: var(--navy); border-color: var(--navy); color: #fff; }

    .ac-grid { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.9rem; grid-template-columns: repeat(auto-fill, minmax(min(100%, 300px), 1fr)); }
    .ac-card { display: grid; grid-template-rows: auto 1fr; border-radius: var(--radius-lg); overflow: hidden; background: var(--surface);
      border: 1px solid var(--border); box-shadow: var(--shadow); color: var(--text); text-decoration: none; height: 100%;
      transition: transform 0.15s, box-shadow 0.15s; }
    .ac-card:hover { transform: translateY(-2px); box-shadow: var(--shadow-hover); }
    .ac-media { position: relative; aspect-ratio: 16 / 7; background: var(--surface-tint); }
    .ac-media img { width: 100%; height: 100%; object-fit: cover; display: block; }
    .ac-badge { position: absolute; top: 0.6rem; left: 0.6rem; font-size: 0.72rem; font-weight: 800; padding: 0.2rem 0.6rem; border-radius: 999px; }
    .ac-badge.Draft { background: #fff; color: var(--text-2); }
    .ac-badge.Booked { background: var(--primary); color: #fff; }
    .ac-badge.Done { background: #f5b301; color: #1b1300; }
    .ac-body { padding: 0.85rem 1rem 1rem; display: grid; gap: 0.4rem; align-content: start; }
    .ac-city { margin: 0; font-size: 1.1rem; }
    .ac-line { display: flex; gap: 0.35rem; align-items: center; font-size: 0.85rem; color: var(--text-2); }
    .ac-bar { height: 0.45rem; border-radius: 999px; background: var(--border); overflow: hidden; margin-top: 0.2rem; }
    .ac-bar span { display: block; height: 100%; background: var(--primary); }
    .ac-next { margin-top: 0.3rem; font-size: 0.82rem; font-weight: 700; color: var(--primary-deep); }

    .ac-fav { position: relative; }
    .ac-fav-remove { position: absolute; top: 0.5rem; right: 0.5rem; }
    .ac-empty { text-align: center; padding: 2rem 1rem; display: grid; gap: 0.6rem; justify-items: center; }
    .ac-empty h2 { margin: 0; }
    .ac-actions { display: flex; flex-wrap: wrap; gap: 0.5rem 1.25rem; align-items: center; font-size: 0.85rem; }
    .ac-link { border: 0; background: none; padding: 0; font: inherit; color: var(--muted); cursor: pointer; text-decoration: underline; }
    .ac-danger { border: 0; border-radius: 999px; padding: 0.35rem 0.85rem; font: inherit; font-weight: 600; background: #b91c1c; color: #fff; cursor: pointer; }
  `,
  template: `
    <div class="container page ac">
      @if (auth.user(); as u) {
        <section class="card ac-profile" aria-labelledby="ac-name">
          @if (u.pictureUrl && !pictureFailed()) {
            <img class="ac-avatar" [src]="u.pictureUrl" alt="" referrerpolicy="no-referrer" (error)="pictureFailed.set(true)" />
          } @else {
            <span class="ac-avatar" aria-hidden="true">{{ u.name[0] }}</span>
          }
          <div>
            <p class="eyebrow">{{ 'account.eyebrow' | t }}</p>
            <h1 id="ac-name" class="ac-name">{{ u.name }}</h1>
            <p class="ac-sub">
              @if (u.email) { {{ u.email }} · }
              {{ 'auth.signedInWith' | t: { providers: u.providers.join(', ') } }} · {{ 'account.since' | t: { date: (u.createdAtUtc | ldate: 'MMMM y') } }}
            </p>
          </div>
          <div class="ac-stats">
            <div class="ac-stat"><strong>{{ counts().all }}</strong><span>{{ 'account.statSaved' | t }}</span></div>
            <div class="ac-stat"><strong>{{ counts().Booked + counts().Done }}</strong><span>{{ 'account.statBought' | t }}</span></div>
            <div class="ac-stat"><strong>{{ counts().Done }}</strong><span>{{ 'account.statDone' | t }}</span></div>
            <div class="ac-stat">
              <strong>{{ me()?.rank ? '#' + me()!.rank : '–' }}</strong><span>{{ 'account.statRank' | t }}</span>
            </div>
          </div>
        </section>

        <div class="ac-tabs" role="tablist">
          <button type="button" role="tab" class="ac-tab" [attr.aria-selected]="tab() === 'trips'" (click)="tab.set('trips')">
            <app-icon name="plane" [size]="16" /> {{ 'trip.mine' | t }} <span class="count">{{ counts().all }}</span>
          </button>
          <button type="button" role="tab" class="ac-tab" [attr.aria-selected]="tab() === 'favorites'" (click)="tab.set('favorites')">
            <app-icon name="heart" [size]="16" /> {{ 'account.favorites' | t }} <span class="count">{{ favorites.count() }}</span>
          </button>
        </div>

        @if (tab() === 'trips') {
          <div class="ac-filters" role="group" [attr.aria-label]="'account.filter' | t">
            @for (f of filters; track f.id) {
              <button type="button" class="ac-chip" [attr.aria-pressed]="filter() === f.id" (click)="filter.set(f.id)">
                {{ f.label | t }} ({{ counts()[f.id] }})
              </button>
            }
          </div>
          @if (trips(); as list) {
            @if (shown().length) {
              <ul class="ac-grid">
                @for (c of shown(); track c.trip.tripPlanId) {
                  <li>
                    <a class="ac-card" [routerLink]="i18n.path('plans', c.trip.tripPlanId)">
                      <div class="ac-media">
                        @if (photo(c.trip.destinationCode); as src) {
                          <img [src]="src" [attr.srcset]="srcset(c.trip.destinationCode)" sizes="(min-width: 960px) 360px, 92vw" alt="" loading="lazy" width="600" height="262" />
                        }
                        <span class="ac-badge" [class]="c.state">{{ statusKey(c.state) | t }}</span>
                      </div>
                      <div class="ac-body">
                        <h2 class="ac-city">{{ c.trip.city }}</h2>
                        <span class="ac-line"><app-icon name="calendar" [size]="14" /> {{ c.trip.startDate | ldate: 'd MMM' }} – {{ c.trip.endDate | ldate: 'd MMM y' }}</span>
                        <span class="ac-line"><app-icon name="bed" [size]="14" /> {{ c.trip.stayName ?? ('diary.noStay' | t) }}</span>
                        @if (c.state !== 'Draft') {
                          <span class="ac-line">{{ 'diary.progress' | t: { done: c.trip.completedDays.length, count: c.trip.dayCount } }} · {{ c.progress }}%</span>
                          <div class="ac-bar" aria-hidden="true"><span [style.width.%]="c.progress"></span></div>
                        }
                        <span class="ac-next">{{ nextKey(c.state) | t }} →</span>
                      </div>
                    </a>
                  </li>
                }
              </ul>
            } @else {
              <div class="card ac-empty">
                <span class="icon-badge icon-badge-lg"><app-icon name="plane" [size]="28" /></span>
                <h2>{{ 'account.noTrips' | t }}</h2>
                <p class="muted">{{ 'trip.none' | t }}</p>
                <a class="btn btn-primary" [routerLink]="i18n.path()">{{ 'home.finalBtn' | t }} <app-icon name="arrowRight" [size]="16" /></a>
              </div>
            }
          } @else {
            <div class="skeleton-list" aria-busy="true"><div class="card skeleton"></div></div>
          }
        } @else {
          <p class="small muted">{{ 'saved.note' | t }}</p>
          @if (favorites.trips().length) {
            <ul class="ac-grid">
              @for (f of favorites.trips(); track f.id) {
                <li class="ac-fav">
                  <a class="ac-card" [routerLink]="i18n.path('plans', f.id)">
                    <div class="ac-media">
                      @if (photo(f.code); as src) { <img [src]="src" [attr.srcset]="srcset(f.code)" sizes="(min-width: 960px) 360px, 92vw" alt="" loading="lazy" width="600" height="262" /> }
                    </div>
                    <div class="ac-body">
                      <h2 class="ac-city">{{ f.city }}, {{ f.country }}</h2>
                      <span class="ac-line"><app-icon name="calendar" [size]="14" /> {{ f.departureDate | ldate: 'd MMM' }} – {{ f.returnDate | ldate: 'd MMM y' }}</span>
                      <span class="ac-line">{{ 'saved.totalAtSave' | t: { amount: (f.total | money: f.currency) } }}</span>
                    </div>
                  </a>
                  <button type="button" class="btn btn-light btn-sm ac-fav-remove" (click)="favorites.remove(f.id)" [attr.aria-label]="'saved.remove' | t: { city: f.city }">
                    <app-icon name="x" [size]="14" />
                  </button>
                </li>
              }
            </ul>
          } @else {
            <div class="card ac-empty">
              <span class="icon-badge icon-badge-lg"><app-icon name="heart" [size]="28" /></span>
              <h2>{{ 'saved.emptyTitle' | t }}</h2>
              <p class="muted">{{ 'saved.emptyText' | t }}</p>
            </div>
          }
        }

        <section class="card ac-actions" [attr.aria-label]="'account.settings' | t">
          <button type="button" class="btn btn-ghost btn-sm" (click)="signOut()">{{ 'auth.signOut' | t }}</button>
          <a class="ac-link" [routerLink]="i18n.path('privacy')">{{ 'auth.privacyLink' | t }}</a>
          @if (confirmDelete()) {
            <span>{{ 'auth.deleteConfirm' | t }}
              <button type="button" class="ac-link" (click)="confirmDelete.set(false)">{{ 'auth.deleteKeep' | t }}</button>
              <button type="button" class="ac-danger" (click)="deleteAccount()">{{ 'auth.deleteYes' | t }}</button>
            </span>
          } @else {
            <button type="button" class="ac-link" (click)="confirmDelete.set(true)">{{ 'auth.delete' | t }}</button>
          }
        </section>
      } @else {
        <div class="card ac-empty">
          <span class="icon-badge icon-badge-lg"><app-icon name="users" [size]="28" /></span>
          <h1 class="page-title">{{ 'account.signInTitle' | t }}</h1>
          <p class="muted">{{ 'account.signInText' | t }}</p>
          <button type="button" class="btn btn-primary" (click)="auth.requestSignIn()">{{ 'auth.signIn' | t }}</button>
        </div>
      }
    </div>
  `,
})
export class AccountPage {
  protected readonly auth = inject(AuthService);
  protected readonly i18n = inject(I18nService);
  protected readonly favorites = inject(SavedTripsService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly photo = destinationPhoto;
  protected readonly srcset = destinationSrcset;
  protected readonly tab = signal<Tab>('trips');
  protected readonly filter = signal<TripFilter>('all');
  protected readonly trips = signal<BookedTripDto[] | null>(null);
  protected readonly me = signal<LeaderboardEntryDto | null>(null);
  protected readonly pictureFailed = signal(false);
  protected readonly confirmDelete = signal(false);

  protected readonly filters: { id: TripFilter; label: TranslationKey }[] = [
    { id: 'all', label: 'account.all' },
    { id: 'Draft', label: 'trip.status.Draft' },
    { id: 'Booked', label: 'trip.status.Booked' },
    { id: 'Done', label: 'trip.status.Done' },
  ];

  private readonly cards = computed(() => {
    const today = toIsoDate(new Date());
    return (this.trips() ?? []).map((t) => ({
      trip: t,
      state: stateOf(t),
      progress: tripProgress(tripDays(t.startDate, t.endDate, today, t.completedDays)),
    }));
  });
  protected readonly shown = computed(() => this.cards().filter((c) => this.filter() === 'all' || c.state === this.filter()));
  protected readonly counts = computed(() => {
    const n = { all: 0, Draft: 0, Booked: 0, Done: 0 };
    for (const c of this.cards()) { n.all++; n[c.state]++; }
    return n;
  });

  constructor() {
    effect(() => {
      const signedIn = this.auth.signedIn();
      untracked(() => {
        this.trips.set(null);
        this.me.set(null);
        if (!signedIn) return;
        this.api.getMyTrips().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (t) => this.trips.set(t), error: () => this.trips.set([]) });
        this.api.getLeaderboard(10).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
          next: (b) => this.me.set(b.entries.find((e) => e.isYou) ?? null),
        });
      });
    });
  }

  protected statusKey(state: Exclude<TripFilter, 'all'>): TranslationKey {
    return `trip.status.${state}`;
  }

  protected nextKey(state: Exclude<TripFilter, 'all'>): TranslationKey {
    return state === 'Draft' ? 'account.nextDraft' : state === 'Booked' ? 'account.nextBooked' : 'account.nextDone';
  }

  protected signOut(): void {
    this.auth.signOut();
  }

  protected async deleteAccount(): Promise<void> {
    if (await this.auth.deleteAccount()) void this.router.navigate(this.i18n.path());
  }
}
