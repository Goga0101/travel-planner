import { NewsFile, NewsItem, countBy, dayGroup, filterNews, liveItems, resolveCountry, sortNews, tripNews } from './world-news';

const item = (over: Partial<NewsItem>): NewsItem => ({
  id: 'x', country: 'ES', category: 'events', importance: 'info',
  publishedAt: '2026-11-01T10:00:00Z', expiresAt: '2026-11-20T00:00:00Z',
  sourceId: 'rtve', url: 'https://www.rtve.es/a', originalLanguage: 'es',
  title: { ka: 'ფესტივალი ბარსელონაში', en: 'Festival in Barcelona' },
  summary: { ka: 'მოკლე აღწერა', en: 'Short summary' },
  places: [{ code: 'barcelona', city: { ka: 'ბარსელონა', en: 'Barcelona' }, iata: 'BCN' }],
  ...over,
});

const file = (items: NewsItem[]): NewsFile => ({
  generatedAt: '2026-11-01T12:00:00Z',
  countries: {
    ES: { name: { ka: 'ესპანეთი', en: 'Spain' }, region: 'europe' },
    JP: { name: { ka: 'იაპონია', en: 'Japan' }, region: 'asia' },
  },
  sources: {},
  items,
});

describe('world news', () => {
  it('drops stories that have expired since the file was built', () => {
    const f = file([item({ id: 'a' }), item({ id: 'b', expiresAt: '2026-10-30T00:00:00Z' })]);
    expect(liveItems(f, Date.parse('2026-11-01T12:00:00Z')).map((i) => i.id)).toEqual(['a']);
  });

  it('puts alerts first, then notices, newest first within each', () => {
    const items = [
      item({ id: 'info-new', publishedAt: '2026-11-01T12:00:00Z' }),
      item({ id: 'alert-old', importance: 'alert', publishedAt: '2026-10-28T12:00:00Z' }),
      item({ id: 'notice', importance: 'notice', publishedAt: '2026-10-30T12:00:00Z' }),
      item({ id: 'alert-new', importance: 'alert', publishedAt: '2026-10-31T12:00:00Z' }),
    ];
    expect(sortNews(items, 'important').map((i) => i.id)).toEqual(['alert-new', 'alert-old', 'notice', 'info-new']);
    expect(sortNews(items, 'newest').map((i) => i.id)).toEqual(['info-new', 'alert-new', 'notice', 'alert-old']);
  });

  it('filters by topic, region and country, and searches both languages', () => {
    const items = [
      item({ id: 'es-events' }),
      item({ id: 'jp-weather', country: 'JP', category: 'weather', title: { ka: 'ტაიფუნი', en: 'Typhoon' }, places: [] }),
    ];
    const f = file(items);
    const none = { category: null, region: null, country: null, query: '' };
    expect(filterNews(items, f, { ...none, category: 'weather' }).map((i) => i.id)).toEqual(['jp-weather']);
    expect(filterNews(items, f, { ...none, region: 'europe' }).map((i) => i.id)).toEqual(['es-events']);
    expect(filterNews(items, f, { ...none, country: 'JP' }).map((i) => i.id)).toEqual(['jp-weather']);
    expect(filterNews(items, f, { ...none, query: 'barcelona' }).map((i) => i.id)).toEqual(['es-events']);
    expect(filterNews(items, f, { ...none, query: 'იაპონია' }).map((i) => i.id)).toEqual(['jp-weather']);
  });

  it('groups by the reader’s calendar day', () => {
    const now = new Date(2026, 10, 3, 15, 0);
    expect(dayGroup(new Date(2026, 10, 3, 1, 0).toISOString(), now)).toBe('today');
    expect(dayGroup(new Date(2026, 10, 2, 23, 0).toISOString(), now)).toBe('yesterday');
    expect(dayGroup(new Date(2026, 9, 29, 12, 0).toISOString(), now)).toBe('week');
    expect(dayGroup(new Date(2026, 9, 20, 12, 0).toISOString(), now)).toBe('older');
  });

  it('counts per key', () => {
    const counts = countBy([item({}), item({}), item({ category: 'rules' })], (i) => i.category);
    expect(counts.get('events')).toBe(2);
    expect(counts.get('rules')).toBe(1);
  });

  it('finds the country from a country, city or airport code', () => {
    const f = { ...file([]), lookup: { cities: { barcelona: 'ES' }, airports: { BCN: 'ES' }, countryNames: {} } };
    expect(resolveCountry(f, { country: 'JP' })).toBe('JP');
    expect(resolveCountry(f, { city: 'barcelona' })).toBe('ES');
    expect(resolveCountry(f, { airport: 'bcn' })).toBe('ES');
    expect(resolveCountry(f, { airport: 'XXX' })).toBeNull();
  });

  it('shows a trip only its country, alerts first, then the trip city before other places', () => {
    const now = Date.parse('2026-11-01T12:00:00Z');
    const items = [
      item({ id: 'es-madrid', publishedAt: '2026-11-01T11:00:00Z', places: [{ code: 'madrid', city: { ka: 'მადრიდი', en: 'Madrid' }, iata: 'MAD' }] }),
      item({ id: 'es-barcelona', publishedAt: '2026-10-31T11:00:00Z' }),
      item({ id: 'es-alert', importance: 'alert', publishedAt: '2026-10-30T11:00:00Z', places: [] }),
      item({ id: 'jp', country: 'JP' }),
    ];
    expect(tripNews(file(items), 'ES', 'barcelona', now).map((i) => i.id)).toEqual(['es-alert', 'es-barcelona', 'es-madrid']);
  });
});
