import { createContext, useState, useCallback, useEffect, type ReactNode } from 'react';
import { setClientToken, registerLogoutHandler } from '../../shared/api/client';
import { apiLogin, apiLogout } from '../../shared/api/auth';
import type { AuthState, UserProfile } from '../../shared/types/auth';

export interface AuthContextValue extends AuthState {
  /** Internal — used only by RootInitializer to signal bootstrap complete */
  _setInitializing: (val: boolean) => void;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

const REFRESH_KEY = 'msosync.refresh_token';
const USER_KEY = 'msosync.user';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [user, setUser] = useState<UserProfile | null>(null);
  const [isInitializing, setIsInitializing] = useState(true);

  const clearTokens = useCallback(() => {
    setAccessToken(null);
    setUser(null);
    setClientToken(null);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
  }, []);

  const setTokens = useCallback(
    (token: string, refreshToken: string, profile: UserProfile) => {
      setAccessToken(token);
      setUser(profile);
      setClientToken(token);
      localStorage.setItem(REFRESH_KEY, refreshToken);
      localStorage.setItem(USER_KEY, JSON.stringify(profile));
    },
    [],
  );

  const logout = useCallback(async () => {
    const storedRefresh = localStorage.getItem(REFRESH_KEY);
    clearTokens();
    if (storedRefresh) {
      apiLogout(storedRefresh).catch(() => undefined); // fire and forget
    }
  }, [clearTokens]);

  const login = useCallback(
    async (username: string, password: string) => {
      const result = await apiLogin(username, password);
      const profile: UserProfile = {
        username,
        roles: [],
        expiresAt: result.expiresAt,
      };
      setTokens(result.token, result.refreshToken, profile);
    },
    [setTokens],
  );

  // Register logout handler for Axios interceptor (avoids circular import)
  useEffect(() => {
    registerLogoutHandler(logout);
  }, [logout]);

  const value: AuthContextValue = {
    accessToken,
    user,
    isInitializing,
    login,
    logout,
    setTokens,
    clearTokens,
    _setInitializing: setIsInitializing,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
