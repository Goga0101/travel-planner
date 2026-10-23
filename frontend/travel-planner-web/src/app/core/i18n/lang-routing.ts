import { HttpInterceptorFn } from '@angular/common/http';
import { Injectable, effect, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { CanActivateFn, RedirectFunction, Router, RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslationKey } from './en';
import { I18nService, isLang } from './i18n.service';

/** Validates the `:lang` segment and activates that language; unknown values redirect to the preferred language. */
export const langGuard: CanActivateFn = (route) => {
  const i18n = inject(I18nService);
  const lang = route.paramMap.get('lang');
  if (isLang(lang)) {
    i18n.setLang(lang);
    return true;
  }
  return inject(Router).parseUrl(`/${i18n.preferred()}`);
};

/** Redirects an unprefixed URL (e.g. "/", "/searches/:id") to the same path under the preferred language. */
export const redirectToPreferredLang: RedirectFunction = (route) => {
  const lang = inject(I18nService).preferred();
  const path = route.url.map((s) => s.path).join('/');
  return path ? `/${lang}/${path}` : `/${lang}`;
};

/** Adds Accept-Language to API calls so server-generated texts come back in the current language. */
export const acceptLanguageInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) return next(req);
  const lang = inject(I18nService).lang();
  return next(req.clone({ setHeaders: { 'Accept-Language': lang } }));
};

/** Route `title`s are translation keys; the document title follows language changes. */
@Injectable({ providedIn: 'root' })
export class I18nTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly i18n = inject(I18nService);
  private key: TranslationKey | null = null;

  constructor() {
    super();
    effect(() => {
      this.i18n.lang();
      this.apply();
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.key = (this.buildTitle(snapshot) as TranslationKey | undefined) ?? null;
    this.apply();
  }

  private apply(): void {
    const brand = this.i18n.t('app.brand');
    this.title.setTitle(this.key ? `${this.i18n.t(this.key)} · ${brand}` : brand);
  }
}

