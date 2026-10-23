import { daysBetween, formatDistance, formatMinutes, formatMoney, formatTimeSpan, parseTimeSpanMinutes } from './format';

describe('TimeSpan parsing', () => {
  it('parses hh:mm:ss', () => {
    expect(parseTimeSpanMinutes('03:20:00')).toBe(200);
  });

  it('parses d.hh:mm:ss and fractional seconds', () => {
    expect(parseTimeSpanMinutes('1.02:05:00')).toBe(26 * 60 + 5);
    expect(parseTimeSpanMinutes('00:45:00.1234567')).toBe(45);
  });

  it('rounds seconds to the nearest minute', () => {
    expect(parseTimeSpanMinutes('00:10:31')).toBe(11);
  });

  it('returns null for missing or malformed values', () => {
    expect(parseTimeSpanMinutes(undefined)).toBeNull();
    expect(parseTimeSpanMinutes('PT3H')).toBeNull();
  });
});

describe('formatting', () => {
  it('formats durations in English', () => {
    expect(formatMinutes(200)).toBe('3h 20m');
    expect(formatMinutes(45)).toBe('45m');
    expect(formatMinutes(120)).toBe('2h');
    expect(formatMinutes(null)).toBe('—');
    expect(formatTimeSpan('02:35:00')).toBe('2h 35m');
  });

  it('formats durations in Georgian', () => {
    expect(formatMinutes(200, 'ka')).toBe('3სთ 20წთ');
    expect(formatMinutes(45, 'ka')).toBe('45წთ');
    expect(formatMinutes(120, 'ka')).toBe('2სთ');
    expect(formatTimeSpan('02:35:00', 'ka')).toBe('2სთ 35წთ');
  });

  it('formats money with the given currency and handles unknown amounts', () => {
    expect(formatMoney(1234.5, 'EUR')).toMatch(/1[,.\s  ]?234[.,]50/);
    expect(formatMoney(1234.5, 'EUR')).toMatch(/€|EUR/);
    expect(formatMoney(1234.5, 'EUR', 'ka')).toMatch(/1[\s  .]?234,50/);
    expect(formatMoney(undefined, 'EUR')).toBe('—');
  });

  it('formats distances per language', () => {
    expect(formatDistance(500)).toBe('500 m');
    expect(formatDistance(500, 'ka')).toBe('500 მ');
  });

  it('computes whole days between ISO dates independent of time zone', () => {
    expect(daysBetween('2026-11-20', '2026-11-24')).toBe(4);
    expect(daysBetween('2026-03-28', '2026-03-30')).toBe(2); // across a DST change in Europe
    expect(daysBetween('bad', '2026-11-24')).toBeNull();
  });
});
