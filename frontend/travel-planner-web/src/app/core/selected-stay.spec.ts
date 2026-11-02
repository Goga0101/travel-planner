import { findPriced, sitePrice, tripTotal, typicalPrice } from './selected-stay.service';

describe('tripTotal', () => {
  const stay = (total: number, currency = 'EUR') => ({ name: 'Hotel', latitude: 1, longitude: 2, price: { total, nightly: total / 4, currency } });

  it('adds the picked hotel estimate to the known costs and the remaining budget', () => {
    expect(tripTotal(325, 1000, 'EUR', stay(400))).toEqual({ known: 325, stay: stay(400).price, total: 725, remaining: 275 });
  });

  it('leaves the total alone without a hotel, without its price or in another currency', () => {
    expect(tripTotal(325, 1000, 'EUR', null).total).toBe(325);
    expect(tripTotal(325, 1000, 'EUR', { name: 'Hotel', latitude: 1, longitude: 2 }).stay).toBeNull();
    expect(tripTotal(325, 1000, 'EUR', stay(400, 'USD'))).toMatchObject({ stay: null, total: 325, remaining: 675 });
  });

  it('can go over the budget', () => {
    expect(tripTotal(800, 1000, 'EUR', stay(400)).remaining).toBe(-200);
  });
});

describe('site prices', () => {
  const rate = (code: string, name: string, nightly: number) => ({ code, name, nightly, viaTripAdvisor: false });
  const hotel = (over: object) => ({ name: 'Hotel Roma', lowestNightly: 90, lowestAgency: 'Agoda', stayTotal: 360, rates: [rate('Agoda', 'Agoda', 90), rate('BookingCom', 'Booking.com', 100)], latitude: 41.9, longitude: 12.5, ...over });
  const list = (hotels: object[]) => ({ source: 'Xotelo', currency: 'EUR', retrievedAtUtc: '2026-11-02T10:00:00Z', nights: 4, rooms: 1, adults: 2,
    lowestNightly: 90, typicalNightly: 120, estimatedStayFrom: 360, estimatedStayTypical: 480, estimatedTripTotal: 0, hotels }) as never;

  it('prefers the Booking.com price and names the site', () => {
    expect(sitePrice(list([]), hotel({}) as never)).toEqual({ total: 400, nightly: 100, currency: 'EUR', source: 'Booking.com', retrievedAtUtc: '2026-11-02T10:00:00Z' });
  });

  it('falls back to the cheapest site without Booking.com', () => {
    expect(sitePrice(list([]), hotel({ rates: [rate('Agoda', 'Agoda', 90), rate('Expedia', 'Expedia', 95)] }) as never).source).toBe('Agoda');
  });

  it('finds the picked hotel by name or position, and offers the typical price otherwise', () => {
    const hp = list([hotel({})]);
    expect(findPriced(hp, { name: 'hotel roma ', latitude: 0, longitude: 0 })?.name).toBe('Hotel Roma');
    expect(findPriced(hp, { name: 'Other', latitude: 41.9005, longitude: 12.5005 })?.name).toBe('Hotel Roma');
    expect(findPriced(hp, { name: 'Other', latitude: 41.95, longitude: 12.5 })).toBeNull();
    expect(typicalPrice(hp)).toMatchObject({ total: 480, typical: true });
  });
});
