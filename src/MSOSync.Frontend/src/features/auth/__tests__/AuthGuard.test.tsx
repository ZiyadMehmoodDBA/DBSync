import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { vi, describe, it, expect } from 'vitest';
import { AuthContext, type AuthContextValue } from '../AuthProvider';
import { AuthGuard } from '../AuthGuard';

function makeCtx(overrides: Partial<AuthContextValue> = {}): AuthContextValue {
  return {
    accessToken: null,
    user: null,
    isInitializing: false,
    login: vi.fn(),
    logout: vi.fn(),
    setTokens: vi.fn(),
    clearTokens: vi.fn(),
    _setInitializing: vi.fn(),
    ...overrides,
  };
}

function Wrapper({ ctx }: { ctx: AuthContextValue }) {
  return (
    <AuthContext.Provider value={ctx}>
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route path="/login" element={<span>login-page</span>} />
          <Route element={<AuthGuard />}>
            <Route path="/dashboard" element={<span>dashboard</span>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>
  );
}

describe('AuthGuard', () => {
  it('redirects to /login when no access token', () => {
    render(<Wrapper ctx={makeCtx({ accessToken: null })} />);
    expect(screen.getByText('login-page')).toBeTruthy();
  });

  it('renders Outlet when access token is present', () => {
    render(<Wrapper ctx={makeCtx({ accessToken: 'tok-1' })} />);
    expect(screen.getByText('dashboard')).toBeTruthy();
  });
});
