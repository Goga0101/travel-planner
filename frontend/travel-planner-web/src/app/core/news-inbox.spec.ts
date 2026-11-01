import { buildInbox } from './news-inbox';
import { NewsFile, NewsItem } from './world-news';

const story = (over: Partial<NewsItem>): NewsItem => ({
  id: 'x', country: 'ES', category: 'transport', importance: 'info',
  publishedAt: '2026-11-01T10:00:00Z', expiresAt: '2026-11-20T00:00:00Z',
  sourceId: 'rtve', url: 'https://www.rtve.es/a', originalLanguage: 'es',
  title: { ka: 'სათაური', en: 'Title' }, summary: { ka: 'ტექსტი', en: 'Text' },
  places: [],
  ...over,
});

const file = (items: NewsItem[]): NewsFile => ({ generatedAt: '2026-11-01T12:00:00Z', countries: {}, sources: {}, items });
const madrid = { code: 'madrid', city: { ka: 'მადრიდი', en: 'Madrid' }, iata: 'MAD' };
const now = Date.parse('2026-11-02T12:00:00Z');

describe('news inbox', () => {
  it('groups stories by the viewed city, most recent visit first, alerts pinned apart', () => {
    const f = file([
      story({ id: 'es-madrid', places: [madrid] }),
      story({ id: 'es-general' }),
      story({ id: 'es-alert', importance: 'alert' }),
      story({ id: 'th-1', country: 'TH' }),
      story({ id: 'fr-1', country: 'FR' }),
    ]);
    const inbox = buildInbox(f, [
      { country: 'ES', city: 'madrid', visitedAt: '2026-11-01T09:00:00Z' },
      { country: 'ES', city: 'barcelona', visitedAt: '2026-11-02T09:00:00Z' },
      { country: 'TH', city: 'bangkok', visitedAt: '2026-10-30T09:00:00Z' },
    ], now);

    expect(inbox.alerts.map((s) => s.id)).toEqual(['es-alert']);
    expect(inbox.groups.map((g) => [g.key, g.stories.map((s) => s.id)])).toEqual([
      ['barcelona', ['es-general']],
      ['madrid', ['es-madrid']],
      ['bangkok', ['th-1']],
    ]);
  });

  it('leaves out expired stories, unvisited countries and empty groups', () => {
    const f = file([story({ id: 'old', expiresAt: '2026-11-01T00:00:00Z' }), story({ id: 'fr', country: 'FR' })]);
    const inbox = buildInbox(f, [{ country: 'ES', city: null, visitedAt: '2026-11-02T09:00:00Z' }], now);
    expect(inbox.groups).toEqual([]);
    expect(inbox.alerts).toEqual([]);
  });

  it('shows a city visited twice once', () => {
    const f = file([story({ id: 'a' })]);
    const inbox = buildInbox(f, [
      { country: 'ES', city: 'barcelona', visitedAt: '2026-11-01T09:00:00Z' },
      { country: 'ES', city: 'barcelona', visitedAt: '2026-11-02T09:00:00Z' },
    ], now);
    expect(inbox.groups.length).toBe(1);
  });
});
