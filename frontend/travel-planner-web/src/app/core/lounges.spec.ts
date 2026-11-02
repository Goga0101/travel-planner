import { LoungeAirport, LoungeCard, LoungePlace, cardsFor, matchPlaces } from './lounges';

const card = (id: string, programs: LoungeCard['programs'][number]['program'][], bank = 'TBC Bank'): LoungeCard => ({
  id, country: 'GE', bank: { ka: bank, en: bank }, card: id, network: 'Visa', conditions: null, source: 'https://example.com',
  programs: programs.map((program) => ({ program, visitsPerYear: null, unlimited: true, guest: null, dining: null })),
});
const place = (name: string, programs: LoungePlace['programs'], bank?: string): LoungePlace => ({
  name, kind: 'lounge', terminal: null, airside: true, schengen: null, location: null, hours: null, programs, offer: null, source: 'https://example.com', bank,
});

describe('lounge matching', () => {
  it('opens a place for cards of the same network', () => {
    const pp = card('pp', ['PriorityPass']);
    const lk = card('lk', ['LoungeKey']);
    expect(cardsFor(place('A', ['PriorityPass']), [pp, lk]).map((c) => c.id)).toEqual(['pp']);
  });

  it('lets Mastercard Travel Pass and Visa Airport Companion into DragonPass places', () => {
    const mtp = card('mtp', ['MastercardTravelPass']);
    const vac = card('vac', ['VisaAirportCompanion']);
    expect(cardsFor(place('D', ['DragonPass']), [mtp, vac])).toHaveLength(2);
    expect(cardsFor(place('P', ['PriorityPass', 'LoungeKey']), [mtp, vac])).toHaveLength(0);
  });

  it('opens a bank lounge only to that bank\'s cards', () => {
    const tbc = card('tbc', ['VisaAirportCompanion'], 'TBC Bank');
    const bog = card('bog', ['VisaAirportCompanion'], 'Bank of Georgia');
    expect(cardsFor(place('TBC lounge', ['BankOwn'], 'TBC Bank'), [tbc, bog]).map((c) => c.id)).toEqual(['tbc']);
  });

  it('lists the places the cards open first', () => {
    const airport: LoungeAirport = { iata: 'DUB', airport: 'Dublin', city: 'Dublin', country: 'IE', notes: null,
      places: [place('closed', ['DragonPass']), place('open', ['PriorityPass'])] };
    expect(matchPlaces(airport, [card('pp', ['PriorityPass'])]).map((m) => m.place.name)).toEqual(['open', 'closed']);
    expect(matchPlaces(null, [])).toEqual([]);
  });
});
