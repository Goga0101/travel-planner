import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { en } from './en';
import { DICTIONARIES, I18nService, LANG_STORAGE_KEY, preferredLang, switchUrlLang, translate } from './i18n.service';
import { ka } from './ka';
import { langGuard, redirectToPreferredLang } from './lang-routing';

describe('dictionaries', () => {
  it('en and ka define exactly the same keys', () => {
    expect(Object.keys(ka).sort()).toEqual(Object.keys(en).sort());
  });

  it('keep the same placeholders in both languages', () => {
    const placeholders = (s: string) => [...new Set(s.match(/\{\w+\}/g) ?? [])].sort();
    for (const key of Object.keys(en) as (keyof typeof en)[]) {
      expect({ key, p: placeholders(ka[key]) }).toEqual({ key, p: placeholders(en[key]) });
    }
  });

  it('has no empty Georgian strings and uses Georgian script', () => {
    for (const value of Object.values(ka)) expect(value.trim().length).toBeGreaterThan(0);
    expect(ka['search.submit']).toMatch(/[ა-ჿ]/);
  });
});

describe('translate', () => {
  it('interpolates parameters', () => {
    expect(translate(en, 'search.hintManual', { max: 10 })).toBe('Pick up to 10 cities.');
    expect(translate(ka, 'search.hintManual', { max: 10 })).toBe('აირჩიეთ არაუმეტეს 10 ქალაქი.');
  });

  it('chooses English plural forms by count; Georgian has one form', () => {
    expect(translate(en, 'unit.nights', { count: 1 })).toBe('1 night');
    expect(translate(en, 'unit.nights', { count: 4 })).toBe('4 nights');
    expect(translate(ka, 'unit.nights', { count: 4 })).toBe('4 ღამე');
  });

  it('leaves unknown placeholders untouched', () => {
    expect(translate(en, 'search.remove', {})).toBe('Remove {city}');
  });

  it('exposes both dictionaries', () => {
    expect(Object.keys(DICTIONARIES).sort()).toEqual(['en', 'ka']);
  });
});

describe('language preference and URLs', () => {
  it('is Georgian-first unless English was chosen explicitly', () => {
    expect(preferredLang(null)).toBe('ka');
    expect(preferredLang(null, ['en-US'])).toBe('ka');
    expect(preferredLang(null, ['ka-GE'])).toBe('ka');
    expect(preferredLang('en', ['ka-GE'])).toBe('en');
    expect(preferredLang('de')).toBe('ka');
  });

  it('swaps the language prefix and keeps the rest of the URL', () => {
    expect(switchUrlLang('/ka/plans/abc?x=1#top', 'en')).toBe('/en/plans/abc?x=1#top');
    expect(switchUrlLang('/en', 'ka')).toBe('/ka');
    expect(switchUrlLang('/', 'en')).toBe('/en');
    expect(switchUrlLang('/searches/abc', 'en')).toBe('/en/searches/abc');
    expect(switchUrlLang('/english-page', 'ka')).toBe('/ka/english-page');
  });
});

@Component({ template: 'page' })
class DummyPage {}

describe('language routing', () => {
  beforeEach(() => {
    try {
      localStorage.removeItem(LANG_STORAGE_KEY);
    } catch {
      /* storage unavailable */
    }
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: '', pathMatch: 'full', redirectTo: redirectToPreferredLang },
          { path: 'searches/:id', redirectTo: redirectToPreferredLang },
          {
            path: ':lang',
            canActivate: [langGuard],
            children: [
              { path: '', component: DummyPage },
              { path: 'searches/:id', component: DummyPage },
            ],
          },
        ]),
      ],
    });
  });

  it('redirects / and legacy paths to the preferred (Georgian) language', async () => {
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/');
    expect(router.url).toBe('/ka');
    await router.navigateByUrl('/searches/abc');
    expect(router.url).toBe('/ka/searches/abc');
  });

  it('activates the language from the URL and rejects unknown prefixes', async () => {
    const router = TestBed.inject(Router);
    const i18n = TestBed.inject(I18nService);
    await router.navigateByUrl('/en/searches/abc');
    expect(i18n.lang()).toBe('en');
    expect(document.documentElement.lang).toBe('en');
    await router.navigateByUrl('/xx');
    expect(router.url).toBe('/ka');
  });

  it('uses the stored choice when present', async () => {
    TestBed.inject(I18nService).setLang('en', true);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/');
    expect(router.url).toBe('/en');
  });
});
