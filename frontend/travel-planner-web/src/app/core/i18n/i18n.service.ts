import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Dictionary, TranslationKey, en } from './en';
import { ka } from './ka';

export type Lang = 'ka' | 'en';
export const LANGS: readonly Lang[] = ['ka', 'en'];
export const DEFAULT_LANG: Lang = 'ka';
export const LANG_STORAGE_KEY = 'tp.lang';

export const DICTIONARIES: Record<Lang, Dictionary> = { en, ka };
export const INTL_LOCALE: Record<Lang, string> = { ka: 'ka-GE', en: 'en-GB' };
/** Angular locale ids registered for DatePipe-style formatting. */
export const ANGULAR_LOCALE: Record<Lang, string> = { ka: 'ka', en: 'en' };

export type TranslateParams = Record<string, string | number | null | undefined>;

export function isLang(value: unknown): value is Lang {
  return value === 'ka' || value === 'en';
}

/**
 * Looks up and interpolates a message. A value "one|other" picks the form by the `count` param
 * (count === 1 → first form). Unknown placeholders are left untouched.
 */
export function translate(dict: Dictionary, key: TranslationKey, params?: TranslateParams): string {
  let text: string = dict[key] ?? en[key] ?? key;
  if (text.includes('|')) {
    const [one, other] = text.split('|');
    text = Number(params?.['count']) === 1 ? one : (other ?? one);
  }
  if (!params) return text;
  return text.replace(/\{(\w+)\}/g, (match, name: string) => {
    const value = params[name];
    return value === null || value === undefined ? match : String(value);
  });
}

/** Georgian-first: an explicit stored choice wins, otherwise Georgian (also for Georgian browsers). */
export function preferredLang(stored: string | null | undefined, browserLanguages: readonly string[] = []): Lang {
  if (isLang(stored)) return stored;
  if (browserLanguages.some((l) => l.toLowerCase().startsWith('ka'))) return 'ka';
  return DEFAULT_LANG;
}

/** Replaces (or adds) the language prefix of an app URL, keeping path, query and fragment. */
export function switchUrlLang(url: string, lang: Lang): string {
  const match = /^\/(ka|en)(?=[/?#]|$)/.exec(url);
  if (match) return `/${lang}${url.slice(match[0].length)}`;
  return url === '/' || url === '' ? `/${lang}` : `/${lang}${url.startsWith('/') ? url : '/' + url}`;
}

@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly document = inject(DOCUMENT);
  private readonly state = signal<Lang>(DEFAULT_LANG);

  readonly lang = this.state.asReadonly();
  readonly intlLocale = computed(() => INTL_LOCALE[this.state()]);
  readonly angularLocale = computed(() => ANGULAR_LOCALE[this.state()]);

  t(key: TranslationKey, params?: TranslateParams): string {
    return translate(DICTIONARIES[this.state()], key, params);
  }

  /** Router commands prefixed with the current language, e.g. path('plans', id) → ['/', 'ka', 'plans', id]. */
  path(...segments: (string | number)[]): (string | number)[] {
    return ['/', this.state(), ...segments];
  }

  setLang(lang: Lang, persist = false): void {
    this.state.set(lang);
    this.document.documentElement.lang = lang;
    if (persist) {
      try {
        localStorage.setItem(LANG_STORAGE_KEY, lang);
      } catch {
        /* storage unavailable (private mode) – keep the in-memory choice */
      }
    }
  }

  preferred(): Lang {
    let stored: string | null = null;
    try {
      stored = localStorage.getItem(LANG_STORAGE_KEY);
    } catch {
      stored = null;
    }
    const browser = typeof navigator !== 'undefined' ? (navigator.languages ?? [navigator.language]) : [];
    return preferredLang(stored, browser);
  }
}
