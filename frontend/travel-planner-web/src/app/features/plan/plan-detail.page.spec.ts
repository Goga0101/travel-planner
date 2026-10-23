import { buildEmbedUrl } from './plan-detail.page';

describe('buildEmbedUrl', () => {
  it('builds a fixed google.com embed URL from coordinates', () => {
    const url = buildEmbedUrl('k&y', 41.2974, 2.0833, 41.3874, 2.1686)!;
    expect(url.startsWith('https://www.google.com/maps/embed/v1/directions?')).toBe(true);
    expect(url).toContain('key=k%26y');
    expect(url).toContain('origin=41.297400,2.083300');
    expect(url).toContain('mode=transit');
  });

  it('returns null without a key or with invalid coordinates', () => {
    expect(buildEmbedUrl('', 1, 2, 3, 4)).toBeNull();
    expect(buildEmbedUrl('key', Number.NaN, 2, 3, 4)).toBeNull();
  });
});
