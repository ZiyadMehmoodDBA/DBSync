import { describe, it, expect, vi, beforeEach } from 'vitest';
import axios from 'axios';

vi.mock('axios', async (importOriginal) => {
  const actual = await importOriginal<typeof import('axios')>();
  return {
    ...actual,
    default: {
      ...actual.default,
      create: vi.fn(() => ({
        interceptors: {
          request: { use: vi.fn() },
          response: { use: vi.fn() },
        },
      })),
      post: vi.fn(),
      isAxiosError: actual.default.isAxiosError,
    },
  };
});

// Re-import after mock is set up
import { setClientToken, getClientToken, registerLogoutHandler } from '../client';

beforeEach(() => {
  setClientToken(null);
  localStorage.clear();
});

describe('client token bridge', () => {
  it('setClientToken / getClientToken round-trip', () => {
    setClientToken('my-token');
    expect(getClientToken()).toBe('my-token');
  });

  it('setClientToken(null) clears the token', () => {
    setClientToken('tok');
    setClientToken(null);
    expect(getClientToken()).toBeNull();
  });
});

describe('single-flight refresh', () => {
  it('registers logout handler without error', () => {
    const fn = vi.fn();
    expect(() => registerLogoutHandler(fn)).not.toThrow();
  });
});

// Suppress unused import warning from vi.mock
void axios;
