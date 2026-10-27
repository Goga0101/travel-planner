import { DayItemDto, PlanDayDto } from '../../core/models';
import { Stop, buildChain, distanceMeters, leaveFor, walkingMinutes } from './day-planner.component';

const hotel: Stop = { name: 'Hotel', latitude: 37.9755, longitude: 23.7348 };
const cafe: Stop = { name: 'Cafe', latitude: 37.9760, longitude: 23.7360 }; // ~120 m from the hotel
const far: Stop = { name: 'Far', latitude: 38.0100, longitude: 23.7348 }; // ~3.8 km north

const item = (time: string, stop: Stop | null): DayItemDto & { stop: Stop | null } => ({ slot: 'morning', time, alternatives: [], stop });

function day(kind: PlanDayDto['kind'], items: ReturnType<typeof item>[]): PlanDayDto {
  return { number: 1, date: '2026-11-03', kind, items };
}

const stopOf = (i: DayItemDto) => (i as ReturnType<typeof item>).stop;

describe('day chain', () => {
  it('estimates walking along streets', () => {
    expect(distanceMeters(hotel, far)).toBeGreaterThan(3700);
    expect(walkingMinutes(800)).toBe(13);
    expect(walkingMinutes(10)).toBe(1);
  });

  it('leaves early enough to arrive on time, on a 5-minute mark', () => {
    expect(leaveFor('10:00', 400)).toBe('09:45'); // 7 min walk + 5
    expect(leaveFor('00:05', 3000)).toBe('00:00');
  });

  it('walks short hops, rides long ones and returns to the stay', () => {
    const chain = buildChain(day('Full', [item('09:00', cafe), item('10:00', far)]), hotel, stopOf);
    expect(chain.hops[0]?.walk).toBe(true);
    expect(chain.hops[0]?.from.name).toBe('Hotel');
    expect(chain.hops[1]?.walk).toBe(false);
    expect(chain.hops[1]?.from.name).toBe('Cafe');
    expect(chain.back?.to.name).toBe('Hotel');
    expect(chain.back?.leave).toBe('11:30');
    expect(chain.mapUrl).toContain('waypoints=');
  });

  it('skips stops without a location and ends at the airport-bound stop on the departure day', () => {
    const chain = buildChain(day('Departure', [item('09:00', cafe), item('11:00', null), item('12:00', far)]), hotel, stopOf);
    expect(chain.hops[1]).toBeNull();
    expect(chain.hops[2]?.from.name).toBe('Cafe');
    expect(chain.back).toBeNull();
  });
});
