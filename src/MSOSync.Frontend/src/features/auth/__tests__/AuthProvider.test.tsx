import { render, screen, act, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { AuthProvider, AuthContext, type AuthContextValue } from '../AuthProvider';
import * as authApi from '../../../shared/api/auth';
import { useContext, useRef, useEffect } from 'react';

vi.mock('../../../shared/api/auth');

const mockLogin = vi.mocked(authApi.apiLogin);
const mockLogout = vi.mocked(authApi.apiLogout);

function TestConsumer() {
  const ctx = useContext(AuthContext);
  if (!ctx) return null;
  return (
    <div>
      <span data-testid="token">{ctx.accessToken ?? 'null'}</span>
      <span data-testid="user">{ctx.user?.username ?? 'none'}</span>
      <button onClick={() => ctx.login('alice', 'pass')}>login</button>
      <button onClick={() => ctx.logout()}>logout</button>
    </div>
  );
}

beforeEach(() => {
  localStorage.clear();
  vi.clearAllMocks();
});

describe('AuthProvider', () => {
  it('starts with null token and no user', () => {
    render(<AuthProvider><TestConsumer /></AuthProvider>);
    expect(screen.getByTestId('token').textContent).toBe('null');
    expect(screen.getByTestId('user').textContent).toBe('none');
  });

  it('login() stores access token in context and refresh token in localStorage', async () => {
    mockLogin.mockResolvedValue({ token: 'tok-1', refreshToken: 'ref-1', expiresAt: '2099-01-01T00:00:00Z' });
    render(<AuthProvider><TestConsumer /></AuthProvider>);
    await act(async () => { userEvent.click(screen.getByText('login')); });
    await waitFor(() => expect(screen.getByTestId('token').textContent).toBe('tok-1'));
    expect(localStorage.getItem('msosync.refresh_token')).toBe('ref-1');
  });

  it('logout() clears token from context and localStorage', async () => {
    mockLogin.mockResolvedValue({ token: 'tok-1', refreshToken: 'ref-1', expiresAt: '2099-01-01T00:00:00Z' });
    mockLogout.mockResolvedValue(undefined);
    render(<AuthProvider><TestConsumer /></AuthProvider>);
    await act(async () => { userEvent.click(screen.getByText('login')); });
    await waitFor(() => expect(screen.getByTestId('token').textContent).toBe('tok-1'));
    await act(async () => { userEvent.click(screen.getByText('logout')); });
    await waitFor(() => expect(screen.getByTestId('token').textContent).toBe('null'));
    expect(localStorage.getItem('msosync.refresh_token')).toBeNull();
  });

  it('setTokens() updates context and localStorage', async () => {
    const ctxHolder = { current: null as AuthContextValue | null };
    function Grabber() {
      const c = useContext(AuthContext);
      const ref = useRef(ctxHolder);
      useEffect(() => {
        ref.current.current = c;
      });
      return null;
    }
    render(<AuthProvider><Grabber /></AuthProvider>);
    await act(async () => {
      ctxHolder.current!.setTokens('tok-2', 'ref-2', { username: 'bob', roles: ['VIEWER'], expiresAt: '2099-01-01T00:00:00Z' });
    });
    await waitFor(() => expect(localStorage.getItem('msosync.refresh_token')).toBe('ref-2'));
  });
});
