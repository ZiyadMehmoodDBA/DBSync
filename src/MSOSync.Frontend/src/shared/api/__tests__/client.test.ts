import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest';
import axios from 'axios';

// vi.hoisted runs before vi.mock hoisting, so variables declared here
// are available inside the vi.mock factory.
const { fakeClientInstance } = vi.hoisted(() => {
  const inst = Object.assign(vi.fn(), {
    interceptors: {
      request: { use: vi.fn() },
      response: { use: vi.fn() },
    },
  });
  return { fakeClientInstance: inst };
});

vi.mock('axios', async (importOriginal) => {
  const actual = await importOriginal<typeof import('axios')>();
  return {
    ...actual,
    default: {
      ...actual.default,
      create: vi.fn(() => fakeClientInstance),
      post: vi.fn(),
      isAxiosError: actual.default.isAxiosError,
    },
  };
});

// Re-import after mock is set up
import { setClientToken, getClientToken, registerLogoutHandler } from '../client';

// Capture the 401 error handler that client.ts registered at module-load time.
// Must be captured BEFORE any beforeEach that calls vi.clearAllMocks(), since
// clearAllMocks wipes mock.calls and the handler would no longer be accessible.
let responseErrorHandler: (error: unknown) => Promise<unknown>;

{
  const useMock = fakeClientInstance.interceptors.response.use as Mock;
  const calls = useMock.mock.calls;
  if (calls.length) {
    responseErrorHandler = calls[0][1] as (error: unknown) => Promise<unknown>;
  }
}

beforeEach(() => {
  setClientToken(null);
  localStorage.clear();
  vi.clearAllMocks();
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

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Build a minimal AxiosError-shaped object the interceptor will recognise. */
function makeAxiosError(status: number, config: Record<string, unknown> = {}) {
  const mergedConfig = { url: '/test', method: 'get', headers: {}, ...config };
  return {
    isAxiosError: true,
    response: { status, data: {}, headers: {} },
    config: mergedConfig,
  };
}

describe('401 single-flight interceptor', () => {
  beforeEach(() => {
    // Ensure axios.isAxiosError identifies our fake errors correctly.
    // We patch the real isAxiosError on the mock to always return true
    // for objects that carry the isAxiosError flag.
    vi.spyOn(axios, 'isAxiosError').mockImplementation(
      (e): e is import('axios').AxiosError =>
        typeof e === 'object' && e !== null && (e as { isAxiosError?: boolean }).isAxiosError === true,
    );
  });

  it('retries a single 401 by refreshing once', async () => {
    if (!responseErrorHandler) throw new Error('response interceptor not captured');
    // Arrange: refresh succeeds, retry returns 200
    const newToken = 'new-access-token';
    (axios.post as Mock).mockResolvedValueOnce({
      data: {
        token: newToken,
        refreshToken: 'new-refresh',
        expiresAt: '2099-01-01T00:00:00Z',
      },
    });
    (fakeClientInstance as Mock).mockResolvedValueOnce({ status: 200, data: 'ok' });

    localStorage.setItem('msosync.refresh_token', 'stored-refresh');

    const result = await responseErrorHandler(makeAxiosError(401));

    // refresh was called once
    expect(axios.post).toHaveBeenCalledTimes(1);
    expect(axios.post).toHaveBeenCalledWith('/api/v1/auth/refresh', {
      refreshToken: 'stored-refresh',
    });
    // original request was retried
    expect(fakeClientInstance).toHaveBeenCalledTimes(1);
    expect(result).toEqual({ status: 200, data: 'ok' });
    // token was updated
    expect(getClientToken()).toBe(newToken);
  });

  it('5 concurrent 401s trigger exactly 1 refresh call', async () => {
    if (!responseErrorHandler) throw new Error('response interceptor not captured');
    // Arrange: refresh resolves after a tick; all 5 retries return 200
    localStorage.setItem('msosync.refresh_token', 'stored-refresh');

    let resolveRefresh!: () => void;
    const refreshDeferred = new Promise<void>((res) => {
      resolveRefresh = res;
    });

    (axios.post as Mock).mockReturnValueOnce(
      refreshDeferred.then(() => ({
        data: {
          token: 'tok-refreshed',
          refreshToken: 'rf2',
          expiresAt: '2099-01-01T00:00:00Z',
        },
      })),
    );
    (fakeClientInstance as Mock).mockResolvedValue({ status: 200, data: 'ok' });

    // Fire 5 concurrent 401s — each with a fresh config object (no _retry flag)
    const promises = Array.from({ length: 5 }, () =>
      responseErrorHandler(makeAxiosError(401, { _retry: undefined })),
    );

    // Let the refresh complete
    resolveRefresh();
    await Promise.all(promises);

    // Only 1 call to the refresh endpoint despite 5 concurrent 401s
    expect(axios.post).toHaveBeenCalledTimes(1);
    // All 5 requests were retried
    expect(fakeClientInstance).toHaveBeenCalledTimes(5);
  });

  it('calls registered logout handler when refresh fails', async () => {
    if (!responseErrorHandler) throw new Error('response interceptor not captured');
    localStorage.setItem('msosync.refresh_token', 'stored-refresh');

    // Refresh fails
    (axios.post as Mock).mockRejectedValueOnce(new Error('network error'));

    const logoutFn = vi.fn();
    registerLogoutHandler(logoutFn);

    await expect(responseErrorHandler(makeAxiosError(401))).rejects.toThrow();

    expect(logoutFn).toHaveBeenCalledTimes(1);
  });
});
