import axios from 'axios';

// Module augmentation so TypeScript accepts _retry without casting to any
declare module 'axios' {
  interface AxiosRequestConfig {
    _retry?: boolean;
  }
}

// Token bridge — avoids circular import with AuthProvider
let _accessToken: string | null = null;
let _onLogout: (() => void) | null = null;

export function setClientToken(token: string | null): void {
  _accessToken = token;
}

export function getClientToken(): string | null {
  return _accessToken;
}

export function registerLogoutHandler(fn: () => void): void {
  _onLogout = fn;
}

// Single-flight refresh lock
let refreshPromise: Promise<void> | null = null;

async function doRefresh(): Promise<void> {
  const storedRefreshToken = localStorage.getItem('msosync.refresh_token');
  if (!storedRefreshToken) {
    _onLogout?.();
    throw new Error('No refresh token');
  }
  try {
    const { data } = await axios.post<{ token: string; refreshToken: string; expiresAt: string }>(
      '/api/v1/auth/refresh',
      { refreshToken: storedRefreshToken },
    );
    setClientToken(data.token);
    localStorage.setItem('msosync.refresh_token', data.refreshToken);
    const stored = localStorage.getItem('msosync.user');
    if (stored) {
      const user = JSON.parse(stored) as { username: string; roles: string[]; expiresAt: string };
      user.expiresAt = data.expiresAt;
      localStorage.setItem('msosync.user', JSON.stringify(user));
    }
  } catch {
    _onLogout?.();
    throw new Error('Refresh failed');
  }
}

const client = axios.create({ baseURL: '/api/v1' });

client.interceptors.request.use((config) => {
  const token = getClientToken();
  if (token) {
    config.headers = config.headers ?? {};
    config.headers['Authorization'] = `Bearer ${token}`;
  }
  return config;
});

client.interceptors.response.use(
  (res) => res,
  async (error: unknown) => {
    if (!axios.isAxiosError(error)) throw error;
    const config = error.config;
    if (!config || error.response?.status !== 401 || config._retry) throw error;
    config._retry = true;
    if (!refreshPromise) {
      refreshPromise = doRefresh().finally(() => {
        refreshPromise = null;
      });
    }
    await refreshPromise;
    config.headers = config.headers ?? {};
    config.headers['Authorization'] = `Bearer ${getClientToken()}`;
    return client(config);
  },
);

export default client;
