import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TripDay, tripDays, tripProgress } from '../../core/booked-trips';
import { ApiError, toApiError } from '../../core/errors';
import { toIsoDate } from '../../core/format';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { BookedTripDto } from '../../core/models';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe } from '../../shared/pipes';

/**
 * "I booked this trip", then one chip per day to tick off while travelling (days that have not begun stay locked).
 * With every day ticked the trip is complete and the traveller appears on the home page leaderboard. Needs a sign-in:
 * signed out, the button opens the account menu and the trip is marked as soon as the sign-in succeeds.
 */
@Component({
  selector: 'app-trip-diary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, LocalDatePipe, RouterLink, ErrorBannerComponent],
  styles: `
    :host { display: block; margin-bottom: var(--gap); }
    .dy { display: grid; gap: 0.9rem; }
    .dy-head { display: flex; gap: 0.9rem; align-items: flex-start; }
    .dy-icon { flex: none; width: 2.6rem; height: 2.6rem; border-radius: 999px; display: grid; place-items: center; font-size: 1.25rem;
      background: var(--primary-soft); color: var(--primary-deep); }
    .dy-head h2 { margin: 0; font-size: 1.15rem; }
    .dy-head p { margin: 0.15rem 0 0; }
    .dy-start { display: flex; flex-wrap: wrap; gap: 0.5rem 1rem; align-items: center; }
    .dy-progress { display: grid; gap: 0.35rem; }
    .dy-progress-head { display: flex; justify-content: space-between; gap: 1rem; font-size: 0.88rem; }
    .dy-bar { height: 0.55rem; border-radius: 999px; background: var(--border); overflow: hidden; }
    .dy-bar span { display: block; height: 100%; border-radius: inherit; background: linear-gradient(90deg, var(--primary), var(--primary-deep));
      transition: width 0.35s ease; }
    .dy-days { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.45rem; grid-template-columns: repeat(auto-fill, minmax(6.4rem, 1fr)); }
    .dy-day { width: 100%; min-height: 3.4rem; display: grid; gap: 0.05rem; justify-items: start; padding: 0.45rem 0.65rem; text-align: left;
      border: 1.5px solid var(--border); border-radius: var(--radius-md); background: var(--surface); color: var(--text); font: inherit; cursor: pointer;
      transition: border-color 0.15s, background 0.15s, transform 0.15s; }
    .dy-day:not(:disabled):hover { border-color: var(--primary); transform: translateY(-1px); }
    .dy-day .n { font-weight: 700; font-size: 0.88rem; display: flex; gap: 0.3rem; align-items: center; }
    .dy-day .d { font-size: 0.76rem; color: var(--muted); text-transform: capitalize; }
    .dy-day.done { border-color: var(--primary); background: var(--primary-soft); color: var(--primary-deep); }
    .dy-day.done .d { color: inherit; opacity: 0.8; }
    .dy-day:disabled { cursor: not-allowed; opacity: 0.55; border-style: dashed; }
    .dy-win { display: flex; gap: 0.9rem; align-items: center; flex-wrap: wrap; padding: 0.9rem 1rem; border-radius: var(--radius-md);
      background: linear-gradient(120deg, #fff4e0, #ffe2b8); color: #7a4300; animation: dy-pop 0.4s ease-out; }
    .dy-win .cup { font-size: 2rem; line-height: 1; }
    .dy-win strong { display: block; font-size: 1.02rem; }
    .dy-win a { margin-left: auto; font-weight: 700; color: inherit; }
    @keyframes dy-pop { from { opacity: 0; transform: scale(0.97); } }
    .dy-foot { display: flex; flex-wrap: wrap; gap: 0.4rem 1rem; align-items: center; justify-content: space-between; font-size: 0.82rem; }
    .dy-link { border: 0; background: none; padding: 0; font: inherit; color: var(--muted); cursor: pointer; text-decoration: underline; }
    .dy-link:hover { color: var(--text); }
    .dy-danger { border: 0; border-radius: 999px; padding: 0.35rem 0.85rem; font: inherit; font-weight: 600; background: #b91c1c; color: #fff; cursor: pointer; }
  `,
  template: `
    <section class="card dy" aria-labelledby="dy-title">
      <div class="dy-head">
        <span class="dy-icon" aria-hidden="true">{{ trip()?.completedAtUtc ? '🏆' : '🧳' }}</span>
        <div>
          <h2 id="dy-title">{{ 'diary.title' | t }}</h2>
          <p class="small muted">{{ (trip() ? 'diary.leadBooked' : 'diary.lead') | t }}</p>
        </div>
      </div>

      @if (trip(); as tr) {
        <div class="dy-progress">
          <div class="dy-progress-head">
            <span>{{ 'diary.progress' | t: { done: doneCount(), count: days().length } }}</span>
            <strong class="tabular">{{ progress() }}%</strong>
          </div>
          <div class="dy-bar" role="progressbar" aria-valuemin="0" aria-valuemax="100" [attr.aria-valuenow]="progress()"
               [attr.aria-label]="'diary.progressLabel' | t"><span [style.width.%]="progress()"></span></div>
        </div>

        <ul class="dy-days" [attr.aria-label]="'diary.daysLabel' | t">
          @for (d of days(); track d.date) {
            <li>
              <button type="button" class="dy-day" [class.done]="d.done" [disabled]="d.locked || busyDay() === d.date"
                      [attr.aria-pressed]="d.done" (click)="toggleDay(d)"
                      [title]="(d.locked ? 'diary.locked' : d.done ? 'diary.untick' : 'diary.tick') | t">
                <span class="n">
                  @if (d.done) { <app-icon name="check" [size]="14" /> } @else if (d.locked) { <span aria-hidden="true">🔒</span> }
                  {{ 'diary.day' | t: { n: d.number } }}
                </span>
                <span class="d">{{ d.date | ldate: 'EEE d MMM' }}</span>
              </button>
            </li>
          }
        </ul>

        @if (tr.completedAtUtc) {
          <div class="dy-win" role="status">
            <span class="cup" aria-hidden="true">🏆</span>
            <span>
              <strong>{{ 'diary.doneTitle' | t: { city: tr.city } }}</strong>
              <span class="small">{{ 'diary.doneText' | t }}</span>
            </span>
            <a [routerLink]="i18n.path()" fragment="leaderboard">{{ 'diary.toBoard' | t }} →</a>
          </div>
        } @else if (nextDay(); as nd) {
          <p class="small muted">{{ 'diary.next' | t: { date: (nd.date | ldate: 'EEEE d MMMM') } }}</p>
        }

        <div class="dy-foot">
          <span class="muted">{{ 'diary.bookedAt' | t: { date: (tr.bookedAtUtc | ldate: 'd MMM y') } }}</span>
          @if (confirmCancel()) {
            <span>
              {{ 'diary.cancelConfirm' | t }}
              <button type="button" class="dy-link" (click)="confirmCancel.set(false)">{{ 'diary.cancelKeep' | t }}</button>
              <button type="button" class="dy-danger" (click)="cancel()">{{ 'diary.cancelYes' | t }}</button>
            </span>
          } @else {
            <button type="button" class="dy-link" (click)="confirmCancel.set(true)">{{ 'diary.cancel' | t }}</button>
          }
        </div>
      } @else {
        <div class="dy-start">
          <button type="button" class="btn btn-primary" (click)="start()" [disabled]="starting() || loading()">
            @if (starting()) { <span class="spinner" aria-hidden="true"></span> } @else { <app-icon name="check" [size]="16" /> }
            {{ 'diary.booked' | t }}
          </button>
          @if (!auth.signedIn()) { <span class="small muted">{{ 'diary.signInNote' | t }}</span> }
        </div>
      }
      <app-error-banner [error]="error()" />
    </section>
  `,
})
export class TripDiaryComponent {
  readonly planId = input.required<string>();

  protected readonly auth = inject(AuthService);
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly trip = signal<BookedTripDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly starting = signal(false);
  protected readonly busyDay = signal<string | null>(null);
  protected readonly confirmCancel = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  /** "I booked" was pressed while signed out: mark the trip once the sign-in succeeds. */
  private readonly startAfterSignIn = signal(false);

  protected readonly days = computed<TripDay[]>(() => {
    const t = this.trip();
    return t ? tripDays(t.startDate, t.endDate, toIsoDate(new Date()), t.completedDays) : [];
  });
  protected readonly doneCount = computed(() => this.days().filter((d) => d.done).length);
  protected readonly progress = computed(() => tripProgress(this.days()));
  protected readonly nextDay = computed(() => this.days().find((d) => !d.done) ?? null);

  constructor() {
    effect(() => {
      const id = this.planId();
      const signedIn = this.auth.signedIn();
      untracked(() => {
        this.trip.set(null);
        this.confirmCancel.set(false);
        if (!signedIn) return;
        if (this.startAfterSignIn()) {
          this.startAfterSignIn.set(false);
          this.start();
          return;
        }
        this.loading.set(true);
        this.api.getMyTrips().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
          next: (all) => { this.trip.set(all.find((t) => t.tripPlanId === id) ?? null); this.loading.set(false); },
          error: () => this.loading.set(false),
        });
      });
    });
  }

  protected start(): void {
    this.error.set(null);
    if (!this.auth.signedIn()) {
      this.startAfterSignIn.set(true);
      this.auth.requestSignIn();
      return;
    }
    this.starting.set(true);
    this.api.startTrip(this.planId()).subscribe({
      next: (t) => { this.trip.set(t); this.starting.set(false); },
      error: (e) => { this.error.set(toApiError(e)); this.starting.set(false); },
    });
  }

  protected toggleDay(day: TripDay): void {
    if (day.locked) return;
    this.error.set(null);
    this.busyDay.set(day.date);
    this.api.setTripDay(this.planId(), day.date, !day.done).subscribe({
      next: (t) => { this.trip.set(t); this.busyDay.set(null); },
      error: (e) => { this.error.set(toApiError(e)); this.busyDay.set(null); },
    });
  }

  protected cancel(): void {
    this.error.set(null);
    this.api.cancelTrip(this.planId()).subscribe({
      next: () => { this.trip.set(null); this.confirmCancel.set(false); },
      error: (e) => this.error.set(toApiError(e)),
    });
  }
}
