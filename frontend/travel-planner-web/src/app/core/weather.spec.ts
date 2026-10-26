import { parseWeather, weatherIcon, weatherRequest } from './weather';

describe('weather', () => {
  it('uses the forecast when the whole trip is within 16 days', () => {
    const r = weatherRequest(41.3874, 2.1686, '2026-11-01', '2026-11-05', '2026-10-26');
    expect(r.kind).toBe('forecast');
    expect(r.url).toContain('https://api.open-meteo.com/v1/forecast?latitude=41.3874&longitude=2.1686');
    expect(r.url).toContain('start_date=2026-11-01&end_date=2026-11-05');
  });

  it('falls back to last year on the same dates for trips further out', () => {
    const r = weatherRequest(41.3874, 2.1686, '2026-12-20', '2026-12-24', '2026-10-26');
    expect(r.kind).toBe('lastYear');
    expect(r.url).toContain('https://archive-api.open-meteo.com/v1/archive?');
    expect(r.url).toContain('start_date=2025-12-20&end_date=2025-12-24');
  });

  it('maps 29 February to 28 February of the previous year', () => {
    expect(weatherRequest(0, 0, '2028-02-29', '2028-03-02', '2026-10-26').url).toContain('start_date=2027-02-28');
  });

  it('maps WMO codes to icons', () => {
    expect(weatherIcon(0)).toBe('sun');
    expect(weatherIcon(3)).toBe('cloud');
    expect(weatherIcon(61)).toBe('rain');
    expect(weatherIcon(73)).toBe('snow');
    expect(weatherIcon(null)).toBe('cloud');
  });

  it('rounds temperatures and drops days without data', () => {
    const days = parseWeather('lastYear', {
      daily: {
        time: ['2025-11-21', '2025-11-22'],
        weather_code: [2, null],
        temperature_2m_max: [17.6, null],
        temperature_2m_min: [9.2, 8],
        precipitation_sum: [0.4, 0],
      },
    });
    expect(days).toEqual([{ date: '2025-11-21', icon: 'cloud', maxC: 18, minC: 9, precipitation: 0.4 }]);
  });
});
