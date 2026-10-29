import { AirlinesService } from './airlines';

describe('AirlinesService.codeOf', () => {
  it('takes the airline part of a flight number and keeps a bare code', () => {
    expect(AirlinesService.codeOf('GQ785')).toBe('GQ');
    expect(AirlinesService.codeOf('a9 123')).toBe('A9');
    expect(AirlinesService.codeOf('W6')).toBe('W6');
  });
});
