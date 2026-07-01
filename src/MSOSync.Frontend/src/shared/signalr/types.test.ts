import { describe, it, expect } from 'vitest';
import { RECONNECT_DELAYS } from './types';

describe('RECONNECT_DELAYS', () => {
  it('matches the specified reconnect contract', () => {
    expect(RECONNECT_DELAYS).toEqual([0, 2_000, 5_000, 10_000, 30_000]);
  });
});
