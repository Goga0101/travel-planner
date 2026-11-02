import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TripDay, tripDays, tripProgress } from '../../core/booked-trips';
import { ApiError, toApiError } from '../../core/errors';
import { toIsoDate } from '../../core/format';
import { TranslationKey } from '../../core/i18n/en';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { BookedTripDto, SaveTripDetails } from '../../core/models';
import { SelectedStayService } from '../../core/selected-stay.service';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { IconComponent } from '../../shared/icon.component';
import { LocalDatePipe } from '../../shared/pipes';

type Step = 'saved' | 'booked' | 'done';

/**
 * "My trip" on the plan page, in three steps: save the plan to "My trips" as a draft (with the hotel picked), confirm once
 * the tickets and the stay are bought (then reminder e-mails go out on the departure day and the second morning), and tick
 * off each day during the trip; with every day ticked the traveller appears on the home page leaderboard. Needs a sign-in:
 * signed out, the button opens the account menu and the trip is saved as soon as the sign-in succeeds.
 */
@Component({
  selector: 'app-trip-diary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, IconComponent, LocalDatePipe, RouterLink, ErrorBannerComponent],
  styles: `
    :host { display: block; margin-bottom: var(--gap); }
    .dy { display: grid; gap: 1rem; }
    .dy-head { display: flex; gap: 0.9rem; align-items: flex-start; }
    .dy-icon { flex: none; width: 2.6rem; height: 2.6rem; border-radius: 999px; display: grid; place-items: center; font-size: 1.25rem;
      background: var(--primary-soft); color: var(--primary-deep); }
    .dy-head h2 { margin: 0; font-size: 1.15rem; display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; }
    .dy-head p { margin: 0.15rem 0 0; }
    .dy-status { font-size: 0.7rem; font-weight: 700; padding: 0.12rem 0.55rem; border-radius: 999px; letter-spacing: 0.02em; }
    .dy-status.Draft { background: var(--surface-tint); color: var(--text-2); border: 1px dashed var(--border-strong); }
    .dy-status.Booked { background: var(--primary); color: #fff; }

    .dy-steps { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 0.5rem; counter-reset: s; }
    .dy-step { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 0.5rem; align-items: center; padding: 0.55rem 0.7rem;
      border-radius: var(--radius-md); background: var(--surface-tint); border: 1px solid var(--border); font-size: 0.8rem; color: var(--muted); }
    .dy-step .n { width: 1.6rem; height: 1.6rem; border-radius: 50%; display: grid; place-items: center; font-weight: 800; background: var(--surface);
      border: 1.5px solid var(--border-strong); color: var(--muted); }
    .dy-step strong { display: block; color: var(--text); font-size: 0.85rem; }
    .dy-step.done .n { background: var(--primary); border-color: var(--primary); color: #fff; }
    .dy-step.current { border-color: var(--primary); background: var(--primary-soft); }
    .dy-step.current .n { border-color: var(--primary); color: var(--primary-deep); }
    @media (max-width: 599px) { .dy-steps { grid-template-columns: 1fr; } }

    .dy-row { display: flex; flex-wrap: wrap; gap: 0.5rem 1rem; align-items: center; }
    .dy-facts { display: flex; flex-wrap: wrap; gap: 0.4rem; }
    .dy-mail { display: flex; gap: 0.6rem; align-items: flex-start; padding: 0.7rem 0.85rem; border-radius: var(--radius-md); background: var(--surface-tint);
      font-size: 0.85rem; color: var(--text-2); }
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
    @let tr = trip();
    <section class="card dy" aria-labelledby="dy-title">
      <div class="dy-head">
        <span class="dy-icon" aria-hidden="true">{{ tr?.completedAtUtc ? '🏆' : tr?.status === 'Booked' ? '🧳' : '📌' }}</span>
        <div>
          <h2 id="dy-title">{{ 'diary.title' | t }}
            @if (tr) { <span class="dy-status" [class]="tr.status">{{ (tr.status === 'Booked' ? 'trip.status.Booked' : 'trip.status.Draft') | t }}</span> }
          </h2>
          <p class="small muted">{{ lead() | t }}</p>
        </div>
      </div>

      <ol class="dy-steps" [attr.aria-label]="'diary.stepsLabel' | t">
        @for (s of steps; track s.id; let i = $index) {
          <li class="dy-step" [class.done]="stepDone(s.id)" [class.current]="currentStep() === s.id">
            <span class="n" aria-hidden="true">@if (stepDone(s.id)) { ✓ } @else { {{ i + 1 }} }</span>
            <span><strong>{{ s.title | t }}</strong>{{ s.text | t }}</span>
          </li>
        }
      </ol>

      @if (!tr) {
        <div class="dy-row">
          <button type="button" class="btn btn-primary" (click)="save()" [disabled]="busy() || loading()">
            @if (busy()) { <span class="spinner" aria-hidden="true"></span> } @else { <app-icon name="heart" [size]="16" /> }
            {{ 'diary.save' | t }}
          </button>
          <span class="small muted">{{ (auth.signedIn() ? 'diary.saveNote' : 'diary.signInNote') | t }}</span>
        </div>
      } @else if (tr.status === 'Draft') {
        <div class="dy-facts">
          <span class="meta-chip"><app-icon name="calendar" [size]="14" /> {{ tr.startDate | ldate: 'd MMM' }} – {{ tr.endDate | ldate: 'd MMM y' }}</span>
          @if (tr.departureLocal) { <span class="meta-chip"><app-icon name="plane" [size]="14" /> {{ tr.departureLocal | ldate: 'd MMM HH:mm' }}</span> }
          <span class="meta-chip"><app-icon name="bed" [size]="14" /> {{ tr.stayName ?? ('diary.noStay' | t) }}</span>
        </div>
        <div class="dy-row">
          <button type="button" class="btn btn-primary" (click)="setBooked(true)" [disabled]="busy()">
            @if (busy()) { <span class="spinner" aria-hidden="true"></span> } @else { <app-icon name="check" [size]="16" /> }
            {{ 'diary.confirm' | t }}
          </button>
          <span class="small muted">{{ 'diary.confirmNote' | t }}</span>
        </div>
      } @else {
        <div class="dy-mail">
          <span aria-hidden="true">✉️</span>
          <span>
            @if (auth.user()?.email; as mail) { {{ 'diary.mailNote' | t: { email: mail } }} } @else { {{ 'diary.noMail' | t }} }
          </span>
        </div>

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
          <p class="small muted">{{ (nd.locked ? 'diary.firstDay' : 'diary.next') | t: { date: (nd.date | ldate: 'EEEE d MMMM') } }}</p>
        }
      }

      @if (tr) {
        <div class="dy-foot">
          <span class="muted">
            {{ (tr.bookedAtUtc ? 'diary.bookedAt' : 'diary.savedAt') | t: { date: ((tr.bookedAtUtc ?? tr.savedAtUtc) | ldate: 'd MMM y') } }}
          </span>
          @if (confirmRemove()) {
            <span>
              {{ (tr.status === 'Booked' ? 'diary.unbookConfirm' : 'diary.removeConfirm') | t }}
              <button type="button" class="dy-link" (click)="confirmRemove.set(false)">{{ 'diary.keep' | t }}</button>
              <button type="button" class="dy-danger" (click)="tr.status === 'Booked' ? setBooked(false) : remove()">{{ 'diary.yes' | t }}</button>
            </span>
          } @else {
            <button type="button" class="dy-link" (click)="confirmRemove.set(true)">
              {{ (tr.status === 'Booked' ? 'diary.unbook' : 'diary.remove') | t }}
            </button>
          }
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
  private readonly stays = inject(SelectedStayService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly trip = signal<BookedTripDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly busyDay = signal<string | null>(null);
  protected readonly confirmRemove = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  /** "Save" was pressed while signed out: save the trip once the sign-in succeeds. */
  private readonly saveAfterSignIn = signal(false);

  protected readonly steps: { id: Step; title: TranslationKey; text: TranslationKey }[] = [
    { id: 'saved', title: 'diary.step1', text: 'diary.step1Text' },
    { id: 'booked', title: 'diary.step2', text: 'diary.step2Text' },
    { id: 'done', title: 'diary.step3', text: 'diary.step3Text' },
  ];

  protected readonly currentStep = computed<Step | null>(() => {
    const t = this.trip();
    return !t ? 'saved' : t.status === 'Draft' ? 'booked' : t.completedAtUtc ? null : 'done';
  });
  protected readonly lead = computed<TranslationKey>(() => {
    const t = this.trip();
    return !t ? 'diary.lead' : t.status === 'Draft' ? 'diary.leadDraft' : 'diary.leadBooked';
  });
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
        this.confirmRemove.set(false);
        if (!signedIn) return;
        if (this.saveAfterSignIn()) {
          this.saveAfterSignIn.set(false);
          this.save();
          return;
        }
        this.loading.set(true);
        this.api.getMyTrips().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
          next: (all) => { this.trip.set(all.find((t) => t.tripPlanId === id) ?? null); this.loading.set(false); },
          error: () => this.loading.set(false),
        });
      });
    });
    // A hotel picked (or changed) after saving goes into the saved trip too.
    effect(() => {
      this.stays.all();
      const t = this.trip();
      const stay = this.stays.get(this.planId());
      if (t && stay && stay.name !== t.stayName) untracked(() => this.api.saveTrip(this.planId(), this.details()).subscribe({ next: (r) => this.trip.set(r) }));
    });
  }

  protected stepDone(step: Step): boolean {
    const t = this.trip();
    return step === 'saved' ? !!t : step === 'booked' ? t?.status === 'Booked' : !!t?.completedAtUtc;
  }

  protected save(): void {
    this.error.set(null);
    if (!this.auth.signedIn()) {
      this.saveAfterSignIn.set(true);
      this.auth.requestSignIn();
      return;
    }
    this.run(this.api.saveTrip(this.planId(), this.details()));
  }

  protected setBooked(booked: boolean): void {
    this.confirmRemove.set(false);
    this.run(this.api.setTripBooked(this.planId(), booked));
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

  protected remove(): void {
    this.error.set(null);
    this.api.cancelTrip(this.planId()).subscribe({
      next: () => { this.trip.set(null); this.confirmRemove.set(false); },
      error: (e) => this.error.set(toApiError(e)),
    });
  }

  private details(): SaveTripDetails {
    const stay = this.stays.get(this.planId());
    return { stayName: stay?.name, stayLatitude: stay?.latitude, stayLongitude: stay?.longitude, lang: this.i18n.lang() };
  }

  private run(request: ReturnType<ApiService['saveTrip']>): void {
    this.error.set(null);
    this.busy.set(true);
    request.subscribe({
      next: (t) => { this.trip.set(t); this.busy.set(false); },
      error: (e) => { this.error.set(toApiError(e)); this.busy.set(false); },
    });
  }
}
