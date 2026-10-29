import { ChangeDetectionStrategy, Component, ElementRef, HostListener, computed, effect, inject, signal, viewChild } from '@angular/core';
import { AuthService } from '../core/auth.service';
import { I18nService } from '../core/i18n/i18n.service';
import { TranslatePipe } from '../core/i18n/translate.pipe';
import { RouterLink } from '@angular/router';

const PANEL_BUTTON_WIDTH = 272;

/**
 * The top-right account control. Signed out: "Sign in", opening a panel with Google and Facebook. Signed in: the person's
 * photo (or initials) and name, opening their details and "Sign out".
 */
@Component({
  selector: 'app-account-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, RouterLink],
  styles: `
    :host { position: relative; display: inline-flex; }
    .am-trigger { display: inline-flex; align-items: center; gap: 0.45rem; min-height: 2.5rem; padding: 0.25rem 0.85rem 0.25rem 0.3rem;
      border: 1px solid var(--border-strong); border-radius: 999px; background: var(--surface); color: var(--text); font: inherit;
      font-size: 0.88rem; font-weight: 600; cursor: pointer; transition: border-color 0.15s, box-shadow 0.15s; }
    .am-trigger:hover { border-color: var(--primary); }
    .am-trigger[aria-expanded='true'] { border-color: var(--primary); box-shadow: 0 0 0 3px var(--primary-ring); }
    .am-trigger.out { padding-left: 0.6rem; background: var(--navy); border-color: var(--navy); color: #fff; }
    .am-trigger.out:hover { background: var(--primary-deep); border-color: var(--primary-deep); }
    .am-avatar { width: 2rem; height: 2rem; border-radius: 50%; object-fit: cover; flex: none; display: inline-grid; place-items: center;
      background: var(--primary-soft); color: var(--primary-deep); font-weight: 700; font-size: 0.85rem; }
    .am-avatar.lg { width: 3rem; height: 3rem; font-size: 1.1rem; }
    .am-name { max-width: 8rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

    .am-panel { position: absolute; z-index: 40; top: calc(100% + 10px); right: 0; width: 20rem; padding: 1.25rem; border-radius: var(--radius-lg);
      background: var(--surface); border: 1px solid var(--border); box-shadow: 0 18px 48px rgba(15, 23, 42, 0.18); animation: am-in 0.14s ease-out; }
    .am-panel::before { content: ''; position: absolute; top: -7px; right: 1.4rem; width: 12px; height: 12px; transform: rotate(45deg);
      background: var(--surface); border-left: 1px solid var(--border); border-top: 1px solid var(--border); }
    @keyframes am-in { from { opacity: 0; transform: translateY(-4px); } }
    .am-title { margin: 0; font-size: 1.1rem; font-weight: 700; color: var(--navy); }
    .am-lead { margin: 0.2rem 0 1rem; color: var(--muted); font-size: 0.88rem; }
    .am-buttons { display: grid; gap: 0.6rem; justify-items: center; }
    .am-google { min-height: 44px; width: 272px; display: flex; justify-content: center; }
    .am-btn { width: 272px; min-height: 44px; display: inline-flex; align-items: center; justify-content: center; gap: 0.6rem;
      border-radius: 999px; font: inherit; font-size: 0.92rem; font-weight: 600; cursor: pointer; padding: 0 1rem; }
    .am-btn:disabled { cursor: not-allowed; opacity: 0.55; }
    .am-btn.google { background: #fff; color: #1f1f1f; border: 1px solid #747775; }
    .am-btn.fb { background: #1877f2; color: #fff; border: 1px solid #1877f2; }
    .am-btn.fb:not(:disabled):hover { background: #166fe5; }
    .am-status { margin: 0.8rem 0 0; font-size: 0.85rem; text-align: center; display: flex; gap: 0.4rem; justify-content: center; align-items: center; }
    .am-status.err { color: var(--danger, #b91c1c); }
    .am-note { margin: 1rem 0 0; padding-top: 0.8rem; border-top: 1px solid var(--border); color: var(--muted); font-size: 0.78rem; line-height: 1.45; }
    .am-soon { margin: 0.9rem 0 0; padding: 0.5rem 0.7rem; border-radius: var(--radius-xs); background: var(--warn-soft); color: var(--warn);
      font-size: 0.82rem; font-weight: 600; text-align: center; }
    .am-user { display: flex; gap: 0.75rem; align-items: center; }
    .am-user-text { min-width: 0; }
    .am-user-name { margin: 0; font-weight: 700; overflow-wrap: anywhere; }
    .am-user-mail { margin: 0; color: var(--muted); font-size: 0.85rem; overflow-wrap: anywhere; }
    .am-providers { margin: 0.9rem 0 0; font-size: 0.82rem; color: var(--muted); }
    .am-signout { margin-top: 1rem; width: 100%; }
    .am-delete { margin-top: 0.8rem; padding-top: 0.7rem; border-top: 1px solid var(--border); display: flex; flex-wrap: wrap;
      justify-content: space-between; gap: 0.4rem 1rem; }
    .am-delete-q { margin: 0; width: 100%; font-size: 0.85rem; color: var(--text-2); }
    .am-delete-actions { display: flex; width: 100%; justify-content: flex-end; gap: 0.8rem; align-items: center; }
    .am-link { border: 0; background: none; padding: 0; font: inherit; font-size: 0.8rem; color: var(--muted); cursor: pointer; text-decoration: underline; }
    .am-link:hover { color: var(--text); }
    .am-danger { border: 0; border-radius: 999px; padding: 0.4rem 0.9rem; font: inherit; font-size: 0.82rem; font-weight: 600;
      background: #b91c1c; color: #fff; cursor: pointer; }

    /* Laptops: the menu is long (Georgian especially), so the control is just the icon or the photo; the label is its tooltip. */
    @media (max-width: 1439px) {
      .am-name, .am-trigger.out .am-label { display: none; }
      .am-trigger:not(.out) { padding-right: 0.3rem; }
      .am-trigger.out { padding: 0.25rem 0.55rem; }
    }
    @media (max-width: 899px) {
      .am-trigger.out .am-label { display: inline; }
      .am-trigger.out { padding: 0.25rem 0.85rem 0.25rem 0.6rem; }
    }
    @media (max-width: 599px) {
      .am-name { display: none; }
      .am-trigger:not(.out) { padding-right: 0.3rem; }
    }
    @media (max-width: 479px) {
      .am-trigger.out .am-label { display: none; }
      .am-trigger.out { padding: 0.25rem 0.55rem; }
      /* On a phone the panel spans the screen under the header instead of hanging off the button. */
      .am-panel { position: fixed; top: 4.25rem; left: 0.75rem; right: 0.75rem; width: auto; }
      .am-panel::before { display: none; }
    }
  `,
  template: `
    @let u = auth.user();
    <button type="button" class="am-trigger" [class.out]="!u" (click)="toggle()" [attr.aria-expanded]="open()" aria-haspopup="dialog"
            [title]="u ? u.name : ('auth.signIn' | t)"
            [attr.aria-label]="u ? ('auth.menu' | t: { name: u.name }) : ('auth.signIn' | t)">
      @if (u) {
        @if (u.pictureUrl && !pictureFailed()) {
          <img class="am-avatar" [src]="u.pictureUrl" alt="" referrerpolicy="no-referrer" (error)="pictureFailed.set(true)" />
        } @else {
          <span class="am-avatar" aria-hidden="true">{{ initials() }}</span>
        }
        <span class="am-name">{{ firstName() }}</span>
      } @else {
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true">
          <circle cx="12" cy="8" r="4" /><path d="M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7" />
        </svg>
        <span class="am-label">{{ 'auth.signIn' | t }}</span>
      }
    </button>

    @if (open()) {
      <div class="am-panel" role="dialog" [attr.aria-label]="(u ? 'auth.menu' : 'auth.title') | t: { name: u?.name ?? '' }">
        @if (u) {
          <div class="am-user">
            @if (u.pictureUrl && !pictureFailed()) {
              <img class="am-avatar lg" [src]="u.pictureUrl" alt="" referrerpolicy="no-referrer" (error)="pictureFailed.set(true)" />
            } @else {
              <span class="am-avatar lg" aria-hidden="true">{{ initials() }}</span>
            }
            <div class="am-user-text">
              <p class="am-user-name">{{ u.name }}</p>
              @if (u.email) { <p class="am-user-mail">{{ u.email }}</p> }
            </div>
          </div>
          <p class="am-providers">{{ 'auth.signedInWith' | t: { providers: u.providers.join(', ') } }}</p>
          <button type="button" class="btn btn-ghost am-signout" (click)="signOut()">{{ 'auth.signOut' | t }}</button>
          <div class="am-delete">
            @if (confirmDelete()) {
              <p class="am-delete-q">{{ 'auth.deleteConfirm' | t }}</p>
              <div class="am-delete-actions">
                <button type="button" class="am-link" (click)="confirmDelete.set(false)">{{ 'auth.deleteKeep' | t }}</button>
                <button type="button" class="am-danger" (click)="deleteAccount()">{{ 'auth.deleteYes' | t }}</button>
              </div>
              @if (auth.error(); as e) { <p class="am-status err" role="alert">{{ e | t }}</p> }
            } @else {
              <button type="button" class="am-link" (click)="confirmDelete.set(true)">{{ 'auth.delete' | t }}</button>
              <a class="am-link" [routerLink]="i18n.path('privacy')" (click)="open.set(false)">{{ 'auth.privacyLink' | t }}</a>
            }
          </div>
        } @else {
          <p class="am-title">{{ 'auth.title' | t }}</p>
          <p class="am-lead">{{ 'auth.lead' | t }}</p>
          <div class="am-buttons">
            @if (auth.googleClientId()) {
              <div class="am-google" #googleButton></div>
            } @else {
              <button type="button" class="am-btn google" disabled>
                <svg width="18" height="18" viewBox="0 0 48 48" aria-hidden="true">
                  <path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"/>
                  <path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"/>
                  <path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"/>
                  <path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"/>
                </svg>
                {{ 'auth.google' | t }}
              </button>
            }
            <button type="button" class="am-btn fb" [disabled]="!auth.facebookAppId() || auth.busy()" (click)="auth.signInWithFacebook()">
              <svg width="20" height="20" viewBox="0 0 24 24" aria-hidden="true">
                <path fill="#fff" d="M24 12.07C24 5.4 18.63 0 12 0S0 5.4 0 12.07C0 18.1 4.39 23.1 10.13 24v-8.44H7.08v-3.49h3.05V9.41c0-3.02 1.79-4.69 4.53-4.69 1.31 0 2.68.24 2.68.24v2.97h-1.51c-1.49 0-1.96.93-1.96 1.89v2.25h3.33l-.53 3.49h-2.8V24C19.61 23.1 24 18.1 24 12.07z"/>
              </svg>
              {{ 'auth.facebook' | t }}
            </button>
          </div>
          @if (auth.busy()) {
            <p class="am-status" aria-live="polite"><span class="spinner" aria-hidden="true"></span> {{ 'auth.signingIn' | t }}</p>
          } @else if (auth.error(); as e) {
            <p class="am-status err" role="alert">{{ e | t }}</p>
          }
          @if (!auth.available()) { <p class="am-soon">{{ 'auth.soon' | t }}</p> }
          <p class="am-note">{{ 'auth.privacy' | t }}
            <a [routerLink]="i18n.path('privacy')" (click)="open.set(false)">{{ 'auth.privacyLink' | t }}</a></p>
        }
      </div>
    }
  `,
})
export class AccountMenuComponent {
  protected readonly auth = inject(AuthService);
  protected readonly i18n = inject(I18nService);
  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly open = signal(false);
  protected readonly pictureFailed = signal(false);
  private readonly googleButton = viewChild<ElementRef<HTMLElement>>('googleButton');

  protected readonly firstName = computed(() => this.auth.user()?.name.split(/\s+/)[0] ?? '');
  protected readonly initials = computed(() =>
    (this.auth.user()?.name ?? '').split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') || '?');

  constructor() {
    // Google draws its own button once the panel (and so the placeholder) is on screen, in the page language.
    effect(() => {
      const el = this.googleButton()?.nativeElement;
      const lang = this.i18n.lang();
      if (el) void this.auth.renderGoogleButton(el, lang, PANEL_BUTTON_WIDTH).catch(() => this.auth.error.set('auth.loadFailed'));
    });
    // Signed in: the panel has done its job.
    effect(() => { if (this.auth.signedIn()) this.open.set(false); });
    effect(() => { this.auth.user(); this.pictureFailed.set(false); this.confirmDelete.set(false); });
  }

  protected toggle(): void {
    const next = !this.open();
    this.open.set(next);
    if (next && !this.auth.signedIn()) {
      this.auth.error.set(null);
      this.auth.prepareFacebook();
    }
  }

  protected readonly confirmDelete = signal(false);

  protected async deleteAccount(): Promise<void> {
    if (await this.auth.deleteAccount()) {
      this.confirmDelete.set(false);
      this.open.set(false);
    }
  }

  protected signOut(): void {
    this.auth.signOut();
    this.open.set(false);
  }

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) this.open.set(false);
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.open.set(false);
  }
}
