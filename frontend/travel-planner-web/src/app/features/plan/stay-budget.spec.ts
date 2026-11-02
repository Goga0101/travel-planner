import { bookingWithMaxNightly, nightlyAllowance } from './stay-budget.component';

describe('stay budget', () => {
  it('spreads a whole-stay amount over the nights and rooms', () => {
    expect(nightlyAllowance(200, false, 4, 1)).toBe(50);
    expect(nightlyAllowance(200, false, 4, 2)).toBe(25);
    expect(nightlyAllowance(80, true, 4, 1)).toBe(80);
  });

  it('opens Booking.com with its own nightly price filter', () => {
    const url = bookingWithMaxNightly('https://www.booking.com/searchresults.html?ss=Tbilisi&checkin=2026-11-25', 'EUR', 50.9);
    const q = new URL(url).searchParams;
    expect(q.get('nflt')).toBe('price=EUR-min-50-1');
    expect(q.get('selected_currency')).toBe('EUR');
    expect(q.get('ss')).toBe('Tbilisi');
  });

  it('leaves other links alone', () => {
    expect(bookingWithMaxNightly('https://www.awin1.com/cread.php?x=1', 'EUR', 50)).toBe('https://www.awin1.com/cread.php?x=1');
    expect(bookingWithMaxNightly('not a url', 'EUR', 50)).toBe('not a url');
  });
});
