# Epic 11C Task 2: Frontend Connection Layer + Provider + Types

## Context

You are building the SignalR connection infrastructure on the React frontend. Task 1 (backend) is complete: the hub is running at `/hubs/operations` and pushes `"OperationsEvent"` messages.

**Key facts about the existing codebase:**
- Frontend is at `src/MSOSync.Frontend/` (Vite + React 19 + TypeScript strict)
- `src/MSOSync.Frontend/src/app/providers.tsx` contains `Providers` — currently wraps children in `<QueryClientProvider><AuthProvider>`. SignalRProvider goes inside `<AuthProvider>` and wraps `{children}`.
- `AuthContextValue` (in `src/features/auth/AuthProvider.tsx`) has `accessToken: string | null` property
- `useAuth()` hook is at `src/features/auth/useAuth.ts` — safe to call inside `<AuthProvider>`
- `QueryClient` instance is defined at module scope in `providers.tsx` — import it into SignalRProvider via a passed prop (QueryClient is passed from SignalRProvider → useSignalR for the reconnect sweep)
- Relative imports ONLY in `shared/signalr/` — no `@/` aliases
- `@microsoft/signalr` not yet installed

## Interfaces

**Consumes (from Task 1):**
- Hub at `/hubs/operations`
- Message channel: `"OperationsEvent"`

**Consumes (already in codebase):**
- `useAuth()` → `{ accessToken: string | null }` from `../../features/auth/useAuth`
- `QueryClient` from `@tanstack/react-query`

**Produces:**
- `SignalRContextValue` — consumed by Task 4 (`useSignalRContext()`)
- `SignalRProvider` — mounted in `providers.tsx`, consumed by Tasks 3 + 4
- `OperationsEvent`, `OperationsEventType` — consumed by Tasks 3 + 4
- `RECONNECT_DELAYS` — protected by `types.test.ts`

---

## Files

- Modify: `src/MSOSync.Frontend/package.json` (install @microsoft/signalr)
- Create: `src/MSOSync.Frontend/src/shared/signalr/types.ts`
- Create: `src/MSOSync.Frontend/src/shared/signalr/context.ts`
- Create: `src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts`
- Create: `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx`
- Modify: `src/MSOSync.Frontend/src/app/providers.tsx`
- Create: `src/MSOSync.Frontend/src/shared/signalr/types.test.ts`
- Create: `src/MSOSync.Frontend/src/shared/signalr/useSignalR.test.ts`

---

- [ ] **Step 1: Install `@microsoft/signalr`**

```pwsh
cd src/MSOSync.Frontend
npm install @microsoft/signalr
```

Expected: `@microsoft/signalr` appears in `package.json` dependencies. If a version prompt appears, accept `^8.x`.

- [ ] **Step 2: Create `types.ts`**

Create `src/MSOSync.Frontend/src/shared/signalr/types.ts`:

```ts
export type ConnectionState = 'connected' | 'reconnecting' | 'disconnected';

export interface SignalRContextValue {
  connectionState: ConnectionState;
  lastConnectedAt?: Date;
  lastDisconnectedAt?: Date;
}

export const RECONNECT_DELAYS = [0, 2_000, 5_000, 10_000, 30_000] as const;

export interface OperationsEvent {
  type: OperationsEventType;
  nodeId: string;
  nodeLabel: string | null;
  previousStatus: string | null;
  currentStatus: string | null;
  occurredAt: string; // ISO 8601
  groupId: string | null;
}

export enum OperationsEventType {
  NodeHealthChanged  = 'NodeHealthChanged',
  NodeApproved       = 'NodeApproved',
  NodeRejected       = 'NodeRejected',
  NodeDisabled       = 'NodeDisabled',
  NodeEnabled        = 'NodeEnabled',
  SyncCycleCompleted = 'SyncCycleCompleted',
}
```

Note: `NodeRegistered` is omitted from the frontend enum to match the backend — it will be added in a future epic when the node self-registration endpoint is implemented.

- [ ] **Step 3: Create `context.ts`**

Create `src/MSOSync.Frontend/src/shared/signalr/context.ts`:

```ts
import { createContext, useContext } from 'react';
import type { SignalRContextValue } from './types';

export const SignalRContext = createContext<SignalRContextValue | null>(null);

export function useSignalRContext(): SignalRContextValue {
  const ctx = useContext(SignalRContext);
  if (!ctx) throw new Error('useSignalRContext must be used inside SignalRProvider');
  return ctx;
}
```

- [ ] **Step 4: Create `useSignalR.ts`**

Create `src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts`:

```ts
import { useEffect, useRef, useState, useCallback } from 'react';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import type { QueryClient } from '@tanstack/react-query';
import type { ConnectionState, OperationsEvent } from './types';
import { RECONNECT_DELAYS } from './types';

interface UseSignalROptions {
  getAccessToken: () => string | null;
  isAuthenticated: boolean;
  queryClient: QueryClient;
  onEvent: (event: OperationsEvent) => void;
}

export function useSignalR({
  getAccessToken,
  isAuthenticated,
  queryClient,
  onEvent,
}: UseSignalROptions) {
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected');
  const [lastConnectedAt, setLastConnectedAt] = useState<Date | undefined>();
  const [lastDisconnectedAt, setLastDisconnectedAt] = useState<Date | undefined>();
  const connectionRef = useRef<ReturnType<typeof buildConnection> | null>(null);

  function buildConnection() {
    return new HubConnectionBuilder()
      .withUrl('/hubs/operations', {
        accessTokenFactory: () => getAccessToken() ?? '',
      })
      .withAutomaticReconnect([...RECONNECT_DELAYS])
      .build();
  }

  const startConnection = useCallback(async () => {
    if (connectionRef.current) return;

    const conn = buildConnection();
    connectionRef.current = conn;

    conn.onreconnecting(() => {
      setConnectionState('reconnecting');
      setLastDisconnectedAt(new Date());
    });

    conn.onreconnected(async () => {
      setConnectionState('connected');
      setLastConnectedAt(new Date());
      await queryClient.invalidateQueries();
    });

    conn.onclose(() => {
      setConnectionState('disconnected');
      setLastDisconnectedAt(new Date());
      connectionRef.current = null;
    });

    conn.on('OperationsEvent', (event: OperationsEvent) => {
      onEvent(event);
    });

    try {
      await conn.start();
      setConnectionState('connected');
      setLastConnectedAt(new Date());
    } catch {
      setConnectionState('disconnected');
      connectionRef.current = null;
    }
  }, [getAccessToken, queryClient, onEvent]);

  const stopConnection = useCallback(async () => {
    if (!connectionRef.current) return;
    const conn = connectionRef.current;
    connectionRef.current = null;
    if (conn.state !== HubConnectionState.Disconnected) {
      await conn.stop();
    }
    setConnectionState('disconnected');
    setLastDisconnectedAt(new Date());
  }, []);

  useEffect(() => {
    if (isAuthenticated) {
      void startConnection();
    } else {
      void stopConnection();
    }

    return () => {
      void stopConnection();
    };
  }, [isAuthenticated, startConnection, stopConnection]);

  return { connectionState, lastConnectedAt, lastDisconnectedAt };
}
```

- [ ] **Step 5: Create `SignalRProvider.tsx`**

Create `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx`:

```tsx
import { useCallback, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useAuth } from '../../features/auth/useAuth';
import { SignalRContext } from './context';
import { useSignalR } from './useSignalR';
import type { OperationsEvent } from './types';

interface Props {
  children: ReactNode;
}

export function SignalRProvider({ children }: Props) {
  const { accessToken } = useAuth();
  const queryClient = useQueryClient();

  const getAccessToken = useCallback(() => accessToken, [accessToken]);

  const handleEvent = useCallback((_event: OperationsEvent) => {
    // Routing wired in Task 3 — placeholder keeps the callback stable
  }, []);

  const { connectionState, lastConnectedAt, lastDisconnectedAt } = useSignalR({
    getAccessToken,
    isAuthenticated: accessToken !== null,
    queryClient,
    onEvent: handleEvent,
  });

  return (
    <SignalRContext.Provider value={{ connectionState, lastConnectedAt, lastDisconnectedAt }}>
      {children}
    </SignalRContext.Provider>
  );
}
```

Note: `handleEvent` is a stub — Task 3 will wire `routeToCache` and `routeToToast` into it. Do NOT call them yet (they don't exist).

- [ ] **Step 6: Insert `SignalRProvider` into `providers.tsx`**

Open `src/MSOSync.Frontend/src/app/providers.tsx`. Current content:

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode } from 'react';
import { Toaster } from 'sonner';
import { AuthProvider } from '../features/auth/AuthProvider';

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 30_000 } },
});

export function Providers({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>{children}</AuthProvider>
      <Toaster richColors closeButton position="bottom-right" />
    </QueryClientProvider>
  );
}
```

Replace with:

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode } from 'react';
import { Toaster } from 'sonner';
import { AuthProvider } from '../features/auth/AuthProvider';
import { SignalRProvider } from '../shared/signalr/SignalRProvider';

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 30_000 } },
});

export function Providers({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <SignalRProvider>
          {children}
        </SignalRProvider>
      </AuthProvider>
      <Toaster richColors closeButton position="bottom-right" />
    </QueryClientProvider>
  );
}
```

`SignalRProvider` sits inside `AuthProvider` so `useAuth()` works, but outside the router so it wraps all pages.

- [ ] **Step 7: Write `types.test.ts`**

Create `src/MSOSync.Frontend/src/shared/signalr/types.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { RECONNECT_DELAYS } from './types';

describe('RECONNECT_DELAYS', () => {
  it('matches the specified reconnect contract', () => {
    expect(RECONNECT_DELAYS).toEqual([0, 2_000, 5_000, 10_000, 30_000]);
  });
});
```

This test protects the reconnect contract from accidental changes. If someone changes `RECONNECT_DELAYS`, this test fails, surfacing the spec deviation.

- [ ] **Step 8: Write `useSignalR.test.ts`**

Create `src/MSOSync.Frontend/src/shared/signalr/useSignalR.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient } from '@tanstack/react-query';

// Mock @microsoft/signalr before importing useSignalR
const mockOn = vi.fn();
const mockStart = vi.fn().mockResolvedValue(undefined);
const mockStop = vi.fn().mockResolvedValue(undefined);
const mockOnreconnecting = vi.fn();
const mockOnclose = vi.fn();

let capturedOnreconnected: (() => void) | null = null;

const mockOnreconnected = vi.fn((cb: () => void) => {
  capturedOnreconnected = cb;
});

const mockConnection = {
  on: mockOn,
  start: mockStart,
  stop: mockStop,
  onreconnecting: mockOnreconnecting,
  onreconnected: mockOnreconnected,
  onclose: mockOnclose,
  state: 'Disconnected',
};

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(() => ({
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: vi.fn().mockReturnThis(),
    build: vi.fn(() => mockConnection),
  })),
  HubConnectionState: { Disconnected: 'Disconnected' },
}));

import { useSignalR } from './useSignalR';

describe('useSignalR reconnect recovery', () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient();
    capturedOnreconnected = null;
    vi.clearAllMocks();
    mockStart.mockResolvedValue(undefined);
    mockStop.mockResolvedValue(undefined);
    mockOnreconnected.mockImplementation((cb: () => void) => {
      capturedOnreconnected = cb;
    });
    mockConnection.state = 'Disconnected';
  });

  it('calls invalidateQueries with no filter on reconnect', async () => {
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    renderHook(() =>
      useSignalR({
        getAccessToken: () => 'test-token',
        isAuthenticated: true,
        queryClient,
        onEvent: vi.fn(),
      }),
    );

    // Wait for connection to start
    await act(async () => {
      await Promise.resolve();
    });

    expect(capturedOnreconnected).not.toBeNull();

    // Simulate reconnect
    await act(async () => {
      capturedOnreconnected!();
      await Promise.resolve();
    });

    expect(invalidateSpy).toHaveBeenCalledWith();
    expect(invalidateSpy).toHaveBeenCalledTimes(1);
  });
});
```

- [ ] **Step 9: Run TypeScript type check**

```pwsh
cd src/MSOSync.Frontend
npx tsc -b --noEmit
```

Expected: no errors. Common issues:
- If `useAuth` import fails: verify path `../../features/auth/useAuth` from `shared/signalr/`
- If `@microsoft/signalr` types not found: verify `npm install` completed and `node_modules/@microsoft/signalr` exists
- If `SignalRProvider` import fails in `providers.tsx`: verify file is at `src/shared/signalr/SignalRProvider.tsx`

- [ ] **Step 10: Run Vitest tests**

```pwsh
cd src/MSOSync.Frontend
npm run test
```

Expected: all tests pass including the new `types.test.ts` (1 test) and `useSignalR.test.ts` (1 test) plus all existing tests (dagre-layout 6 tests, etc.). No regressions.

- [ ] **Step 11: Commit**

```pwsh
cd D:\MSOSync
git add src/MSOSync.Frontend/package.json src/MSOSync.Frontend/package-lock.json
git add src/MSOSync.Frontend/src/shared/signalr/types.ts
git add src/MSOSync.Frontend/src/shared/signalr/context.ts
git add src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts
git add src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx
git add src/MSOSync.Frontend/src/app/providers.tsx
git add src/MSOSync.Frontend/src/shared/signalr/types.test.ts
git add src/MSOSync.Frontend/src/shared/signalr/useSignalR.test.ts
git commit -m "feat(11C): add SignalR connection layer, provider, types, reconnect tests"
```

## Report Contract

Return: `DONE`, last commit SHA, `tsc --noEmit` result (clean), test results (count + all pass), any concerns. Write full report to the report file path provided by the coordinator.
