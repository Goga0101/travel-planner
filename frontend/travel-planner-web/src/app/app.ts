import { ChangeDetectionStrategy, Component, effect, inject, untracked } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { ClientConfigService } from './core/client-config.service';
import { I18nService, Lang, switchUrlLang } from './core/i18n/i18n.service';
import { TranslatePipe } from './core/i18n/translate.pipe';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, TranslatePipe],
  template: `
    <a class="skip-link" href="#main">{{ 'app.skip' | t }}</a>
    @if (config.isDemo()) {
      <div class="demo-banner" role="note">
        <strong>{{ 'app.demoBannerStrong' | t }}</strong> – {{ 'app.demoBannerText' | t }}
      </div>
    }
    <header class="topbar">
      <a [routerLink]="i18n.path()" class="brand"><span aria-hidden="true">🧭</span> {{ 'app.brand' | t }}</a>
      <div class="topbar-end">
        @if (config.config(); as c) {
          <span class="small muted mode-label">{{ 'app.dataLabel' | t: { mode: (c.dataMode === 'Demo' ? 'app.dataDemo' : 'app.dataLive') | t } }}</span>
        }
        <nav class="lang-switch" [attr.aria-label]="'app.language' | t">
          @for (l of languages; track l.code) {
            <button type="button" [class.active]="i18n.lang() === l.code" [attr.aria-pressed]="i18n.lang() === l.code"
                    [attr.lang]="l.code" [attr.aria-label]="'app.switchTo' | t: { language: l.name }" (click)="switchLang(l.code)">
              {{ l.short }}
            </button>
          }
        </nav>
      </div>
    </header>
    <main id="main" class="container">
      <router-outlet />
    </main>
    <footer class="footer container small muted">
      <p>{{ 'app.footerNote' | t }}</p>
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
    </footer>
  `,
})
export class App {
  protected readonly config = inject(ClientConfigService);
  protected readonly i18n = inject(I18nService);
  private readonly router = inject(Router);

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
