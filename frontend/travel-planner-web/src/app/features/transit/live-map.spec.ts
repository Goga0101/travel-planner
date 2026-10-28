import { VehicleDto } from '../../core/models';
import { decodePolyline, modeGroup, pointAlong, positionAt } from './live-map.component';

describe('live map', () => {
  it('decodes Google polylines (precision 5)', () => {
    expect(decodePolyline('_p~iF~ps|U_ulLnnqC_mqNvxq`@')).toEqual([
      [38.5, -120.2],
      [40.7, -120.95],
      [43.252, -126.453],
    ]);
  });

  it('finds the point a fraction of the way along a line', () => {
    const line: [number, number][] = [[0, 0], [0, 1], [0, 3]];
    expect(pointAlong(line, 0)).toEqual([0, 0]);
    expect(pointAlong(line, 0.5)).toEqual([0, 1.5]);
    expect(pointAlong(line, 1)).toEqual([0, 3]);
  });

  it('moves a vehicle along its stretch, waits at stops and disappears after its last stop', () => {
    const v: VehicleDto = {
      id: 't', mode: 'BUS', realTime: true,
      stretches: [
        { departure: 100, arrival: 200, to: 'B', shape: '' },
        { departure: 260, arrival: 300, to: 'C', shape: '' },
      ],
    };
    const shapes: [number, number][][] = [[[0, 0], [0, 2]], [[0, 2], [0, 4]]];
    expect(positionAt(v, shapes, 150)).toEqual({ at: [0, 1], next: 'B' });
    expect(positionAt(v, shapes, 230)).toEqual({ at: [0, 2], next: 'C' });
    expect(positionAt(v, shapes, 50)).toEqual({ at: [0, 0], next: 'B' }); // about to leave: waiting at the first stop
    expect(positionAt(v, shapes, 0)).toBeNull();
    expect(positionAt(v, shapes, 400)).toBeNull();
  });

  it('groups transit modes for the map filter', () => {
    expect(modeGroup('SUBWAY')).toBe('metro');
    expect(modeGroup('TRAM')).toBe('tram');
    expect(modeGroup('REGIONAL_RAIL')).toBe('rail');
    expect(modeGroup('BUS')).toBe('bus');
  });
});
