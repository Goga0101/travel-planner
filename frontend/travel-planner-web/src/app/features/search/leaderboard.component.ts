import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../../core/auth.service';
import { ApiService } from '../../core/api.service';
import { TranslationKey } from '../../core/i18n/en';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LeaderboardDto } from '../../core/models';

const MEDALS = ['🥇', '🥈', '🥉'];

/**
 * Home page "Travellers' leaderboard": the top three on a podium, places 4–10 as a list, and how to get on it. Sample
 * travellers are labelled until real ones fill the board; the signed-in traveller's own row is marked "you".
 */
@Component({
  selector: 'app-leaderboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  styles: `
    :host { display: block; }
    .lb { display: grid; gap: 1.5rem; grid-template-columns: minmax(0, 1fr); }
    @media (min-width: 960px) { .lb { grid-template-columns: minmax(0, 1.35fr) minmax(0, 1fr); align-items: start; } }

    .lb-podium { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 0.6rem; align-items: end; margin: 0 0 0.9rem; padding: 0; list-style: none; }
    .lb-place { display: grid; justify-items: center; gap: 0.35rem; text-align: center; min-width: 0; }
    .lb-medal { font-size: 2rem; line-height: 1; }
    .lb-avatar { width: 3.2rem; height: 3.2rem; border-radius: 50%; display: grid; place-items: center; font-weight: 800; font-size: 1.15rem;
      background: var(--surface); color: var(--navy); border: 3px solid var(--border); box-shadow: var(--shadow); }
    .lb-place[data-rank='1'] .lb-avatar { width: 3.8rem; height: 3.8rem; border-color: #f5b301; }
    .lb-place[data-rank='2'] .lb-avatar { border-color: #a8b3c2; }
    .lb-place[data-rank='3'] .lb-avatar { border-color: #c98a4b; }
    .lb-place-name { font-weight: 700; max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lb-step { width: 100%; border-radius: var(--radius-md) var(--radius-md) 0 0; background: var(--navy); color: #fff; padding: 0.6rem 0.4rem;
      display: grid; gap: 0.1rem; justify-items: center; }
    .lb-place[data-rank='1'] .lb-step { min-height: 7.2rem; background: linear-gradient(180deg, #f5b301, #d48f00); color: #1b1300; }
    .lb-place[data-rank='2'] .lb-step { min-height: 5.6rem; }
    .lb-place[data-rank='3'] .lb-step { min-height: 4.6rem; background: var(--primary-deep); }
    .lb-step strong { font-size: 1.35rem; line-height: 1.1; }
    .lb-step span { font-size: 0.75rem; opacity: 0.85; }
    .lb-podium .lb-place:nth-child(1) { order: 2; }
    .lb-podium .lb-place:nth-child(2) { order: 1; }
    .lb-podium .lb-place:nth-child(3) { order: 3; }

    .lb-list { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.4rem; }
    .lb-row { display: grid; grid-template-columns: 2rem minmax(0, 1fr) auto; gap: 0.75rem; align-items: center; padding: 0.6rem 0.85rem;
      border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--surface); }
    .lb-row.you { border-color: var(--primary); box-shadow: 0 0 0 3px var(--primary-ring); }
    .lb-rank { font-weight: 800; color: var(--muted); font-variant-numeric: tabular-nums; text-align: center; }
    .lb-who { min-width: 0; display: grid; gap: 0.2rem; }
    .lb-name { font-weight: 700; display: flex; gap: 0.4rem; align-items: center; flex-wrap: wrap; }
    .lb-codes { display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .lb-code { font-size: 0.68rem; font-weight: 700; letter-spacing: 0.04em; padding: 0.05rem 0.35rem; border-radius: 4px;
      background: var(--surface-tint); color: var(--text-2); border: 1px solid var(--border); }
    .lb-stats { text-align: right; font-size: 0.8rem; color: var(--muted); white-space: nowrap; }
    .lb-stats strong { display: block; font-size: 1rem; color: var(--text); }
    .lb-tag { font-size: 0.66rem; font-weight: 700; padding: 0.08rem 0.4rem; border-radius: 999px; text-transform: uppercase; letter-spacing: 0.03em; }
    .lb-tag.sample { background: var(--surface-tint); color: var(--muted); border: 1px dashed var(--border-strong); }
    .lb-tag.you { background: var(--primary); color: #fff; }
    .lb-sample-note { margin: 0.6rem 0 0; font-size: 0.78rem; color: var(--muted); }

    .lb-how { padding: 1.25rem; border-radius: var(--radius-lg); background: var(--surface); border: 1px solid var(--border); box-shadow: var(--shadow); }
    .lb-how h3 { margin: 0 0 0.9rem; font-size: 1.1rem; }
    .lb-how ol { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.85rem; }
    .lb-how li { display: grid; grid-template-columns: 2.2rem minmax(0, 1fr); gap: 0.7rem; align-items: start; }
    .lb-how .num { width: 2.2rem; height: 2.2rem; border-radius: 50%; display: grid; place-items: center; font-size: 1.1rem; background: var(--primary-soft); }
    .lb-how strong { display: block; }
    .lb-how p { margin: 0.1rem 0 0; }
    .lb-how .btn { margin-top: 1rem; width: 100%; }
  `,
  template: `
    @if (board(); as b) {
      <div class="lb">
        <div>
          <ol class="lb-podium" [attr.aria-label]="'board.podium' | t">
            @for (e of podium(); track e.rank) {
              <li class="lb-place" [attr.data-rank]="e.rank">
                <span class="lb-medal" aria-hidden="true">{{ medal(e.rank) }}</span>
                <span class="lb-avatar" aria-hidden="true">{{ e.name[0] }}</span>
                <span class="lb-place-name" [title]="e.name">{{ e.name }}</span>
                @if (e.isYou) { <span class="lb-tag you">{{ 'board.you' | t }}</span> }
                @else if (e.isSample) { <span class="lb-tag sample">{{ 'board.sample' | t }}</span> }
                <span class="lb-step">
                  <strong>{{ e.trips }}</strong>
                  <span>{{ 'board.tripsWord' | t: { count: e.trips } }}</span>
                  <span>{{ 'board.daysCountries' | t: { days: e.days, countries: e.countries.length } }}</span>
                </span>
              </li>
            }
          </ol>
          <ol class="lb-list" [attr.start]="4">
            @for (e of rest(); track e.rank) {
              <li class="lb-row" [class.you]="e.isYou">
                <span class="lb-rank">{{ e.rank }}</span>
                <span class="lb-who">
                  <span class="lb-name">{{ e.name }}
                    @if (e.isYou) { <span class="lb-tag you">{{ 'board.you' | t }}</span> }
                    @else if (e.isSample) { <span class="lb-tag sample">{{ 'board.sample' | t }}</span> }
                  </span>
                  <span class="lb-codes" [attr.aria-label]="'board.countries' | t">
                    @for (c of e.countries; track c) { <span class="lb-code">{{ c }}</span> }
                  </span>
                </span>
                <span class="lb-stats">
                  <strong>{{ 'board.trips' | t: { count: e.trips } }}</strong>
                  {{ 'board.days' | t: { count: e.days } }}
                </span>
              </li>
            }
          </ol>
          @if (hasSamples()) { <p class="lb-sample-note">{{ 'board.sampleNote' | t }}</p> }
        </div>

        <aside class="lb-how" aria-labelledby="lb-how-title">
          <h3 id="lb-how-title">{{ 'board.howTitle' | t }}</h3>
          <ol>
            @for (s of steps; track s.title) {
              <li>
                <span class="num" aria-hidden="true">{{ s.icon }}</span>
                <span><strong>{{ s.title | t }}</strong><p class="small muted">{{ s.text | t }}</p></span>
              </li>
            }
          </ol>
          @if (!auth.signedIn()) {
            <button type="button" class="btn btn-dark" (click)="auth.requestSignIn()">{{ 'board.signIn' | t }}</button>
          }
        </aside>
      </div>
    }
  `,
})
export class LeaderboardComponent {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly board = signal<LeaderboardDto | null>(null);
  protected readonly podium = computed(() => this.board()?.entries.filter((e) => e.rank <= 3) ?? []);
  protected readonly rest = computed(() => this.board()?.entries.filter((e) => e.rank > 3) ?? []);
  protected readonly hasSamples = computed(() => this.board()?.entries.some((e) => e.isSample) ?? false);

  protected readonly steps: { icon: string; title: TranslationKey; text: TranslationKey }[] = [
    { icon: '👤', title: 'board.step1Title', text: 'board.step1Text' },
    { icon: '✅', title: 'board.step2Title', text: 'board.step2Text' },
    { icon: '🗓️', title: 'board.step3Title', text: 'board.step3Text' },
  ];

  constructor() {
    // Signing in or out changes the "you" row.
    effect(() => {
      this.auth.signedIn();
      untracked(() => this.api.getLeaderboard(10).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (b) => this.board.set(b),
        error: () => this.board.set(null),
      }));
    });
  }

  protected medal(rank: number): string {
    return MEDALS[rank - 1] ?? '';
  }
}
