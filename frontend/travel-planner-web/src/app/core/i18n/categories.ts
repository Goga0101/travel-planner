import { TranslationKey } from './en';

/** Server cost-category codes (English) → translation keys. Unknown codes are shown as-is. */
export const CATEGORY_KEYS: Record<string, TranslationKey> = {
  Flights: 'category.Flights',
  Accommodation: 'category.Accommodation',
  'Airport transfers': 'category.AirportTransfers',
  'Taxes & fees': 'category.TaxesAndFees',
  'Food (estimate)': 'category.Food',
  'Local transport (estimate)': 'category.LocalTransport',
  Contingency: 'category.Contingency',
};

export function categoryLabel(code: string, t: (key: TranslationKey) => string): string {
  const key = CATEGORY_KEYS[code];
  return key ? t(key) : code;
}
