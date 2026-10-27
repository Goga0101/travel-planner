import { ChangeDetectionStrategy, Component, computed, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { PHOTOS } from './core/destination-photos';
import { SavedTripsService } from './core/local-lists';
import { IconComponent } from './shared/icon.component';
import { ClientConfigService } from './core/client-config.service';
import { I18nService, Lang, switchUrlLang } from './core/i18n/i18n.service';
import { TranslatePipe } from './core/i18n/translate.pipe';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, TranslatePipe, IconComponent],
  host: { '[class.is-home]': 'isHome()' },
  template: `
    <a class="skip-link" href="#main">{{ 'app.skip' | t }}</a>
    @if (config.isDemo()) {
      <div class="demo-banner" role="note">
        <strong>{{ 'app.demoBannerStrong' | t }}</strong> – {{ 'app.demoBannerText' | t }}
      </div>
    }
    <header class="topbar">
      <div class="topbar-inner container">
        <a [routerLink]="i18n.path()" class="brand" [attr.aria-label]="'app.brand' | t">
          <svg class="brand-mark" viewBox="0 0 40 28" width="40" height="28" aria-hidden="true">
            <path d="M2 26 14 8l7 10 5-6 12 14z" fill="currentColor" opacity=".9" />
            <path d="M14 8l4 5.7-3 .8-1.5-2.3-2 2.6z" fill="var(--surface)" opacity=".9" />
            <circle cx="30" cy="6" r="3" fill="var(--accent)" />
          </svg>
          <span class="brand-text">
            <span class="brand-name">{{ 'app.brand' | t }}</span>
            <span class="brand-tagline">{{ 'app.tagline' | t }}</span>
          </span>
        </a>
        <nav class="main-nav" [attr.aria-label]="'nav.main' | t">
          <a [routerLink]="i18n.path()">{{ 'nav.home' | t }}</a>
          <a [routerLink]="i18n.path()" fragment="destinations">{{ 'nav.destinations' | t }}</a>
          <a [routerLink]="i18n.path()" fragment="how">{{ 'nav.how' | t }}</a>
          <a [routerLink]="i18n.path()" fragment="about">{{ 'nav.about' | t }}</a>
        </nav>
        <div class="topbar-end">
          <a class="saved-link-header" [routerLink]="i18n.path('saved')" [attr.aria-label]="'saved.headerLabel' | t: { count: saved.count() }">
            <app-icon name="heart" [size]="20" />
            @if (saved.count()) { <span class="count-badge" aria-hidden="true">{{ saved.count() }}</span> }
          </a>
          @if (config.config(); as c) {
            @let mode = (c.dataMode === 'Demo' ? 'app.dataDemo' : 'app.dataLive') | t;
            <span class="mode-label" [class.mode-demo]="c.dataMode === 'Demo'" [title]="'app.dataLabel' | t: { mode }">
              <span class="visually-hidden">{{ 'app.dataLabel' | t: { mode: '' } }}</span>{{ mode }}
            </span>
          }
          <div class="lang-switch" role="group" [attr.aria-label]="'app.language' | t">
            @for (l of languages; track l.code) {
              <button type="button" [class.active]="i18n.lang() === l.code" [attr.aria-pressed]="i18n.lang() === l.code"
                      [attr.lang]="l.code" [attr.aria-label]="'app.switchTo' | t: { language: l.name }" (click)="switchLang(l.code)">
                {{ l.short }}
              </button>
            }
          </div>
        </div>
      </div>
    </header>
    <main id="main">
      <router-outlet />
    </main>
    <footer class="footer">
      <div class="container footer-grid">
        <div>
          <p class="footer-brand">{{ 'app.brand' | t }}</p>
          <p class="small">{{ 'footer.about' | t }}</p>
        </div>
        <nav [attr.aria-label]="'footer.explore' | t">
          <p class="footer-head">{{ 'footer.explore' | t }}</p>
          <ul class="footer-links">
            <li><a [routerLink]="i18n.path()" fragment="destinations">{{ 'nav.destinations' | t }}</a></li>
            <li><a [routerLink]="i18n.path()" fragment="how">{{ 'nav.how' | t }}</a></li>
            <li><a [routerLink]="i18n.path()" fragment="about">{{ 'nav.about' | t }}</a></li>
            <li><a [routerLink]="i18n.path('saved')">{{ 'saved.title' | t }}</a></li>
          </ul>
        </nav>
        <div class="footer-details">
          @if (config.config(); as c) {
            <details>
              <summary>{{ 'app.dataSources' | t }}</summary>
              <ul>
                @for (p of c.providers; track p.category + p.name) {
                  <li>{{ p.category }}: {{ p.name }} – {{ (p.active ? 'app.providerActive' : 'app.providerInactive') | t }}. {{ p.note }}</li>
                }
              </ul>
            </details>
          }
          <details>
            <summary>{{ 'footer.photoCredits' | t }}</summary>
            <p class="small">{{ 'footer.photoNote' | t }}</p>
            <ul>
              @for (p of photoCredits; track p.key) {
                <li><a [href]="p.source" target="_blank" rel="noopener noreferrer">{{ p.key }}</a> – {{ p.author }}, {{ p.license }}</li>
              }
            </ul>
          </details>
        </div>
        <div class="footer-note">
          <strong><app-icon name="check" [size]="16" /> {{ 'footer.disclaimerTitle' | t }}</strong>
          <span>{{ 'app.footerNote' | t }}</span>
        </div>
      </div>
      <div class="container footer-bottom">
        <span>© {{ year }} {{ 'app.brand' | t }}</span>
        @if (config.config(); as c) {
          <span>{{ 'app.dataLabel' | t: { mode: ((c.dataMode === 'Demo' ? 'app.dataDemo' : 'app.dataLive') | t) } }}</span>
        }
      </div>
    </footer>
  `,
})
export class App {
  protected readonly config = inject(ClientConfigService);
  protected readonly year = new Date().getFullYear();
  protected readonly i18n = inject(I18nService);
  protected readonly saved = inject(SavedTripsService);
  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map((e) => e.urlAfterRedirects)),
    { initialValue: this.router.url },
  );
  /** The home page has a full-bleed hero; the header floats over it there. */
  protected readonly isHome = computed(() => /^\/(ka|en)\/?(?:[?#].*)?$/.test(this.url()));
  protected readonly photoCredits = Object.entries(PHOTOS).map(([key, p]) => ({ key, ...p }));

  protected readonly languages: { code: Lang; short: string; name: string }[] = [
    { code: 'ka', short: 'ქართ', name: 'ქართული' },
    { code: 'en', short: 'ENG', name: 'English' },
  ];

  constructor() {
    // Server-provided texts (provider notes, limits) are localized: reload them whenever the language changes.
    effect(() => {
      this.i18n.lang();
      untracked(() => this.config.load());
    });
  }

  protected switchLang(lang: Lang): void {
    if (lang === this.i18n.lang()) return;
    this.i18n.setLang(lang, true);
    void this.router.navigateByUrl(switchUrlLang(this.router.url, lang));
  }
}
