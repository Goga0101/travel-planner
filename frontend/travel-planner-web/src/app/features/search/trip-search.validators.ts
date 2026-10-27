import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { daysBetween } from '../../core/format';
import { TranslationKey } from '../../core/i18n/en';

export interface TripDateInput {
  earliestDeparture: string;
  latestReturn: string;
  durationDays: number | null;
}

/**
 * Cross-field date rules mirroring the backend validator:
 * earliest departure in the future, return after departure, window >= duration, window <= maxWindowDays.
 */
export function validateTripDates(value: TripDateInput, todayIso: string, maxWindowDays: number): ValidationErrors | null {
  const errors: ValidationErrors = {};
  const { earliestDeparture, latestReturn, durationDays } = value;

  if (earliestDeparture) {
    const fromToday = daysBetween(todayIso, earliestDeparture);
    if (fromToday !== null && fromToday <= 0) errors['departureNotFuture'] = true;
    if (fromToday !== null && fromToday > 365) errors['departureTooFar'] = true;
  }

  if (earliestDeparture && latestReturn) {
    const span = daysBetween(earliestDeparture, latestReturn);
    if (span !== null) {
      if (span <= 0) errors['returnBeforeDeparture'] = true;
      else {
        if (span > maxWindowDays) errors['windowTooLong'] = { max: maxWindowDays };
        if (durationDays && span + 1 < durationDays) errors['windowShorterThanDuration'] = true;
      }
    }
  }

  return Object.keys(errors).length ? errors : null;
}

export function tripDatesValidator(todayIso: () => string, maxWindowDays: () => number): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null =>
    validateTripDates(
      {
        earliestDeparture: group.get('earliestDeparture')?.value ?? '',
        latestReturn: group.get('latestReturn')?.value ?? '',
        durationDays: group.get('durationDays')?.value ?? null,
      },
      todayIso(),
      maxWindowDays(),
    );
}

/** Manual mode requires 1..max destinations. */
export function manualDestinationsValidator(max: () => number): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    if (group.get('destinationMode')?.value !== 'Manual') return null;
    const ids = (group.get('destinationIds')?.value as string[] | null) ?? [];
    const cities = (group.get('destinationCityCodes')?.value as string[] | null) ?? [];
    const count = ids.length + cities.length;
    if (count === 0) return { destinationsRequired: true };
    if (count > max()) return { tooManyDestinations: { max: max() } };
    return null;
  };
}

/** Form-level error code → translation key (params come from the error value, e.g. { max }). */
export const DATE_ERROR_KEYS: Record<string, TranslationKey> = {
  departureNotFuture: 'validation.departureNotFuture',
  departureTooFar: 'validation.departureTooFar',
  returnBeforeDeparture: 'validation.returnBeforeDeparture',
  windowTooLong: 'validation.windowTooLong',
  windowShorterThanDuration: 'validation.windowShorterThanDuration',
  destinationsRequired: 'validation.destinationsRequired',
  tooManyDestinations: 'validation.tooManyDestinations',
};
