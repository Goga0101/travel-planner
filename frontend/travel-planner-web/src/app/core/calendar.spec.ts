import { buildIcs } from './calendar';

describe('buildIcs', () => {
  const now = new Date(Date.UTC(2026, 9, 26, 8, 0, 0));

  it('writes one VEVENT per flight with floating local times', () => {
    const ics = buildIcs(
      [
        { uid: 'p1-out@travelvista', title: 'Flight TBS → BCN', start: '2026-11-21T06:10:00', end: '2026-11-21T11:00', location: 'TBS' },
        { uid: 'p1-back@travelvista', title: 'Flight BCN → TBS', start: '2026-11-25T18:40' },
      ],
      now,
    );

    expect(ics.startsWith('BEGIN:VCALENDAR\r\nVERSION:2.0\r\n')).toBe(true);
    expect(ics.match(/BEGIN:VEVENT/g)?.length).toBe(2);
    expect(ics).toContain('DTSTAMP:20261026T080000Z');
    expect(ics).toContain('DTSTART:20261121T061000\r\nDTEND:20261121T110000');
    expect(ics).toContain('DTSTART:20261125T184000\r\nSUMMARY:Flight BCN → TBS');
    expect(ics.trimEnd().endsWith('END:VCALENDAR')).toBe(true);
  });

  it('escapes text values', () => {
    const ics = buildIcs([{ uid: 'x', title: 'A, B; C', start: '2026-11-21T06:10', description: 'line1\nline2' }], now);
    expect(ics).toContain('SUMMARY:A\\, B\\; C');
    expect(ics).toContain('DESCRIPTION:line1\\nline2');
  });
});
