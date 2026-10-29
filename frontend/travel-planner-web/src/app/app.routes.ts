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
      { path: 'flights', title: 'title.flights', loadComponent: () => import('./features/flights/flight-calendar.page').then((m) => m.FlightCalendarPage) },
      { path: 'transit', title: 'title.transit', loadComponent: () => import('./features/transit/transit.page').then((m) => m.TransitPage) },
      { path: 'saved', title: 'title.saved', loadComponent: () => import('./features/saved/saved-trips.page').then((m) => m.SavedTripsPage) },
      { path: 'privacy', title: 'title.privacy', loadComponent: () => import('./features/legal/privacy.page').then((m) => m.PrivacyPage) },
      { path: 'plans/:id', title: 'title.plan', loadComponent: () => import('./features/plan/plan-detail.page').then((m) => m.PlanDetailPage) },
      { path: '**', redirectTo: '' },
    ],
  },
  { path: '**', redirectTo: redirectToPreferredLang },
];
