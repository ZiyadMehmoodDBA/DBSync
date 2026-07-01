import { createContext, useContext } from 'react';
import type { SignalRContextValue } from './types';

export const SignalRContext = createContext<SignalRContextValue | null>(null);

export function useSignalRContext(): SignalRContextValue {
  const ctx = useContext(SignalRContext);
  if (!ctx) throw new Error('useSignalRContext must be used inside SignalRProvider');
  return ctx;
}
