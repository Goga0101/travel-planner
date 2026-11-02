import { tripDays, tripProgress } from './booked-trips';

describe('tripDays', () => {
  it('lists every day of the trip, both ends included', () => {
    const days = tripDays('2026-10-30', '2026-11-02', '2026-12-01');
    expect(days.map((d) => d.date)).toEqual(['2026-10-30', '2026-10-31', '2026-11-01', '2026-11-02']);
    expect(days.map((d) => d.number)).toEqual([1, 2, 3, 4]);
  });

  it('locks the days after today and marks the ticked ones', () => {
    const days = tripDays('2026-11-01', '2026-11-03', '2026-11-02', ['2026-11-01']);
    expect(days.map((d) => [d.done, d.locked])).toEqual([[true, false], [false, false], [false, true]]);
  });

  it('accepts dates with a time part', () => {
    expect(tripDays('2026-11-01T00:00:00', '2026-11-01T00:00:00', '2026-11-01')).toHaveLength(1);
  });
});

describe('tripProgress', () => {
  it('is the share of ticked days', () => {
    expect(tripProgress(tripDays('2026-11-01', '2026-11-04', '2026-11-04', ['2026-11-01']))).toBe(25);
    expect(tripProgress([])).toBe(0);
  });
});
