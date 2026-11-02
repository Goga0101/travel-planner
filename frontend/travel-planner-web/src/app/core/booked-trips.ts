/** One day of a booked trip, as a chip of the trip diary. */
export interface TripDay {
  /** yyyy-MM-dd */
  date: string;
  /** 1-based day of the trip. */
  number: number;
  done: boolean;
  /** Not begun yet: it cannot be ticked. */
  locked: boolean;
}

/** Every day from start to end (yyyy-MM-dd, both included); a day after `today` is locked. */
export function tripDays(start: string, end: string, today: string, completed: readonly string[] = []): TripDay[] {
  const days: TripDay[] = [];
  const done = new Set(completed);
  const last = Date.UTC(...parts(end));
  for (let t = Date.UTC(...parts(start)), n = 1; t <= last && n <= 366; t += 86_400_000, n++) {
    const date = new Date(t).toISOString().slice(0, 10);
    days.push({ date, number: n, done: done.has(date), locked: date > today });
  }
  return days;
}

/** Share of the trip's days ticked, 0–100. */
export function tripProgress(days: readonly TripDay[]): number {
  return days.length ? Math.round((days.filter((d) => d.done).length / days.length) * 100) : 0;
}

function parts(isoDate: string): [number, number, number] {
  const [y, m, d] = isoDate.slice(0, 10).split('-').map(Number);
  return [y!, m! - 1, d!];
}
