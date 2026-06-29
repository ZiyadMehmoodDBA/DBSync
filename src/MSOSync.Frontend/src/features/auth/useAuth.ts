import { useContext } from 'react';
import { AuthContext } from './AuthProvider';
import type { AuthState } from '../../shared/types/auth';

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
