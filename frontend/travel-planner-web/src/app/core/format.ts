import { formatDate, formatNumber } from '@angular/common';

export type FormatLang = 'en' | 'ka';

const INTL: Record<FormatLang, string> = { en: 'en-GB', ka: 'ka-GE' };
const UNITS: Record<FormatLang, { h: string; m: string; km: string; meters: string }> = {
  en: { h: 'h', m: 'm', km: 'km', meters: 'm' },
  ka: { h: 'სთ', m: 'წთ', km: 'კმ', meters: 'მ' },
};

/** Parses a .NET TimeSpan string ("hh:mm:ss", "d.hh:mm:ss", optional fraction) into minutes. */
export function parseTimeSpanMinutes(value: string | null | undefined): number | null {
  if (!value) return null;
  const match = /^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.\d+)?$/.exec(value.trim());
  if (!match) return null;
  const [, negative, days, hours, minutes, seconds] = match;
  const total = Number(days ?? 0) * 1440 + Number(hours) * 60 + Number(minutes) + Math.round(Number(seconds) / 60);
  return negative ? -total : total;
}

/** 200 -> "3h 20m" (en) / "3სთ 20წთ" (ka); 45 -> "45m" / "45წთ". */
export function formatMinutes(minutes: number | null | undefined, lang: FormatLang = 'en'): string {
  if (minutes === null || minutes === undefined || !Number.isFinite(minutes)) return '—';
  const u = UNITS[lang];
  const m = Math.max(0, Math.round(minutes));
  const h = Math.floor(m / 60);
  const rest = m % 60;
  if (h === 0) return `${rest}${u.m}`;
  return rest === 0 ? `${h}${u.h}` : `${h}${u.h} ${rest}${u.m}`;
}

export function formatTimeSpan(value: string | null | undefined, lang: FormatLang = 'en'): string {
  return formatMinutes(parseTimeSpanMinutes(value), lang);
}

export function formatMoney(amount: number | null | undefined, currency: string | null | undefined, lang: FormatLang = 'en'): string {
  if (amount === null || amount === undefined || !Number.isFinite(amount)) return '—';
  if (!currency) return amount.toFixed(2);
  try {
    return new Intl.NumberFormat(INTL[lang], { style: 'currency', currency, maximumFractionDigits: 2 }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currency}`;
  }
}

export function formatDistance(meters: number | null | undefined, lang: FormatLang = 'en'): string {
  if (meters === null || meters === undefined) return '—';
  const u = UNITS[lang];
  return meters >= 1000 ? `${formatNumber(meters / 1000, lang, '1.1-1')} ${u.km}` : `${meters} ${u.meters}`;
}

/** Date formatting with Angular locale data ("ka" is registered in app.config). */
export function formatLocalDate(value: string | number | Date | null | undefined, format: string, lang: FormatLang = 'en'): string {
  if (value === null || value === undefined || value === '') return '';
  try {
    return formatDate(value, format, lang);
  } catch {
    return String(value);
  }
}

/** Formats a Date as yyyy-MM-dd using local calendar fields. */
export function toIsoDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/** Whole days between two yyyy-MM-dd strings (b - a), timezone-independent. */
export function daysBetween(a: string, b: string): number | null {
  const pa = parseIsoDate(a);
  const pb = parseIsoDate(b);
  if (pa === null || pb === null) return null;
  return Math.round((pb - pa) / 86_400_000);
}

function parseIsoDate(value: string): number | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value ?? '');
  return m ? Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : null;
}
