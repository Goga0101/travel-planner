import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import localeKa from '@angular/common/locales/ka';
import { ApplicationConfig, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { TitleStrategy, provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';

import { routes } from './app.routes';
import { I18nTitleStrategy, acceptLanguageInterceptor } from './core/i18n/lang-routing';
import { apiBaseUrlInterceptor, loadRuntimeConfig } from './core/runtime-config';

registerLocaleData(localeKa, 'ka');

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideAppInitializer(() => loadRuntimeConfig()),
    // Order matters: Accept-Language matches relative '/api/' URLs before they are made absolute.
    provideHttpClient(withFetch(), withInterceptors([acceptLanguageInterceptor, apiBaseUrlInterceptor])),
    provideRouter(routes, withComponentInputBinding(), withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    { provide: TitleStrategy, useClass: I18nTitleStrategy },
  ],
};
