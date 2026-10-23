import { validateTripDates } from './trip-search.validators';

const TODAY = '2026-10-20';

describe('validateTripDates', () => {
  it('accepts the reference example (20–30 Nov, 5 days)', () => {
    expect(validateTripDates({ earliestDeparture: '2026-11-20', latestReturn: '2026-11-30', durationDays: 5 }, TODAY, 60)).toBeNull();
  });

  it('accepts a window exactly as long as the trip (5 days = departure + 4)', () => {
    expect(validateTripDates({ earliestDeparture: '2026-11-20', latestReturn: '2026-11-24', durationDays: 5 }, TODAY, 60)).toBeNull();
  });

  it('rejects a window shorter than the trip', () => {
    const errors = validateTripDates({ earliestDeparture: '2026-11-20', latestReturn: '2026-11-23', durationDays: 5 }, TODAY, 60);
    expect(errors?.['windowShorterThanDuration']).toBe(true);
  });

  it('rejects departures today or in the past', () => {
    expect(validateTripDates({ earliestDeparture: TODAY, latestReturn: '2026-11-30', durationDays: 5 }, TODAY, 60)?.['departureNotFuture']).toBe(true);
  });

  it('rejects a return before the departure', () => {
    expect(validateTripDates({ earliestDeparture: '2026-11-20', latestReturn: '2026-11-19', durationDays: 5 }, TODAY, 60)?.['returnBeforeDeparture']).toBe(true);
  });

  it('rejects windows longer than the configured maximum', () => {
    expect(validateTripDates({ earliestDeparture: '2026-11-01', latestReturn: '2027-01-15', durationDays: 5 }, TODAY, 60)?.['windowTooLong']).toEqual({ max: 60 });
  });
});
