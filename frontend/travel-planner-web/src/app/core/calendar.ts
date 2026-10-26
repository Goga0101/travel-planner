/** Minimal iCalendar (RFC 5545) builder for the trip's flights, downloadable as an .ics file. */

export interface CalendarEvent {
  uid: string;
  title: string;
  /** Local date-time "YYYY-MM-DDTHH:mm[:ss]" at the departure airport (written as floating local time). */
  start: string;
  end?: string;
  location?: string;
  description?: string;
  url?: string;
}

function escapeText(value: string): string {
  return value.replace(/\\/g, '\\\\').replace(/;/g, '\\;').replace(/,/g, '\\,').replace(/\r?\n/g, '\\n');
}

function localStamp(iso: string): string {
  const [date, time = '00:00:00'] = iso.split('T');
  const [h = '00', m = '00', s = '00'] = time.split(':');
  return `${date.replace(/-/g, '')}T${h.padStart(2, '0')}${m.padStart(2, '0')}${s.slice(0, 2).padStart(2, '0')}`;
}

function utcStamp(d: Date): string {
  return d.toISOString().replace(/[-:]/g, '').replace(/\.\d{3}/, '');
}

/** Lines longer than 75 octets must be folded (approximated by characters, which is safe for ASCII-heavy content). */
function fold(line: string): string {
  const parts: string[] = [];
  for (let i = 0; i < line.length; i += 73) parts.push((i ? ' ' : '') + line.slice(i, i + 73));
  return parts.join('\r\n');
}

export function buildIcs(events: CalendarEvent[], now = new Date()): string {
  const lines = ['BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:-//TravelVista//Trip planner//EN', 'CALSCALE:GREGORIAN', 'METHOD:PUBLISH'];
  for (const e of events) {
    lines.push('BEGIN:VEVENT', `UID:${e.uid}`, `DTSTAMP:${utcStamp(now)}`, `DTSTART:${localStamp(e.start)}`);
    if (e.end) lines.push(`DTEND:${localStamp(e.end)}`);
    lines.push(`SUMMARY:${escapeText(e.title)}`);
    if (e.location) lines.push(`LOCATION:${escapeText(e.location)}`);
    if (e.description) lines.push(`DESCRIPTION:${escapeText(e.description)}`);
    if (e.url) lines.push(`URL:${e.url}`);
    lines.push('END:VEVENT');
  }
  lines.push('END:VCALENDAR');
  return lines.map(fold).join('\r\n') + '\r\n';
}

export function downloadFile(filename: string, content: string, type: string): void {
  const url = URL.createObjectURL(new Blob([content], { type }));
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
