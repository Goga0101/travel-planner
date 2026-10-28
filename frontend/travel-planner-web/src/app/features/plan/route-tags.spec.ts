import { TransitItineraryDto, TransitLegDto } from '../../core/models';
import { routeTags } from './itinerary.component';

const leg = (mode: string, minutes: number): TransitLegDto =>
  ({ mode, from: 'a', to: 'b', departureTime: '', arrivalTime: '', durationMinutes: minutes }) as TransitLegDto;
const option = (minutes: number, legs: TransitLegDto[]): TransitItineraryDto =>
  ({ durationMinutes: minutes, transfers: 0, departureTime: '', arrivalTime: '', legs }) as TransitItineraryDto;

describe('routeTags', () => {
  it('marks the quickest, the fewest changes, the least walking and the metro options', () => {
    const busMetro = option(54, [leg('WALK', 8), leg('BUS', 23), leg('WALK', 2), leg('SUBWAY', 16), leg('WALK', 6)]);
    const metro = option(55, [leg('WALK', 10), leg('SUBWAY', 13), leg('WALK', 4), leg('SUBWAY', 16), leg('WALK', 5)]);
    const bus = option(67, [leg('WALK', 10), leg('BUS', 42), leg('WALK', 15)]);

    expect(routeTags([busMetro, metro, bus])).toEqual([['fastest', 'leastWalking', 'metro'], ['metro'], ['fewestTransfers']]);
  });

  it('adds no comparison tags when the options do not differ', () => {
    const a = option(30, [leg('WALK', 5), leg('BUS', 20), leg('WALK', 5)]);
    expect(routeTags([a, { ...a }])).toEqual([[], []]);
    expect(routeTags([])).toEqual([]);
  });
});
