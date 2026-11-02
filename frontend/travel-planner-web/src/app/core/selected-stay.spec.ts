import { tripTotal } from './selected-stay.service';

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
