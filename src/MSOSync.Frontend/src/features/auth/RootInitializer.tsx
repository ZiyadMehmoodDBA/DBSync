import { useEffect, useContext } from 'react';
import { Outlet } from 'react-router-dom';
import { AuthContext } from './AuthProvider';
import { apiRefresh } from '../../shared/api/auth';
import { FullscreenLoader } from '../../shared/components/FullscreenLoader';

const REFRESH_KEY = 'msosync.refresh_token';
const USER_KEY = 'msosync.user';

export function RootInitializer() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('RootInitializer must be inside AuthProvider');

  const { isInitializing, setTokens, clearTokens, _setInitializing } = ctx;

  useEffect(() => {
    const storedRefresh = localStorage.getItem(REFRESH_KEY);
    if (!storedRefresh) {
      _setInitializing(false);
      return;
    }

    apiRefresh(storedRefresh)
      .then((result) => {
        const storedUser = localStorage.getItem(USER_KEY);
        const user = storedUser
          ? (JSON.parse(storedUser) as { username: string; roles: string[] })
          : { username: '', roles: [] };
        setTokens(result.token, result.refreshToken, {
          username: user.username,
          roles: user.roles,
          expiresAt: result.expiresAt,
        });
      })
      .catch(() => {
        clearTokens();
      })
      .finally(() => {
        _setInitializing(false);
      });
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  if (isInitializing) {
    return <FullscreenLoader />;
  }

  return <Outlet />;
}
