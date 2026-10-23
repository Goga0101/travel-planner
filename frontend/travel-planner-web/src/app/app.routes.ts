import { Routes } from '@angular/router';
import { langGuard, redirectToPreferredLang } from './core/i18n/lang-routing';

// Every page lives under a language prefix (/ka, /en). Route titles are translation keys (see I18nTitleStrategy).
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: redirectToPreferredLang },
  // Legacy, unprefixed links keep working.
  { path: 'searches/:id', redirectTo: redirectToPreferredLang },
  { path: 'plans/:id', redirectTo: redirectToPreferredLang },
  {
    path: ':lang',
    canActivate: [langGuard],
    children: [
      { path: '', title: 'title.search', loadComponent: () => import('./features/search/trip-search.page').then((m) => m.TripSearchPage) },
      { path: 'searches/:id', title: 'title.results', loadComponent: () => import('./features/results/search-results.page').then((m) => m.SearchResultsPage) },
      { path: 'plans/:id', title: 'title.plan', loadComponent: () => import('./features/plan/plan-detail.page').then((m) => m.PlanDetailPage) },
      { path: '**', redirectTo: '' },
    ],
  },
  { path: '**', redirectTo: redirectToPreferredLang },
];
