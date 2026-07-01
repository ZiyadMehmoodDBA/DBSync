# Epic 11C Task 4: UI Integration + Connection Indicator + Manual Acceptance

## Context

You are adding the SignalR connection state indicator to the app shell and verifying the full feature works in the browser. Tasks 1–3 are complete: the hub is running, SignalRProvider wraps all pages, event routing and notifications work.

**Key facts about the existing codebase:**
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` is the app shell — it has a `<header>` topbar at line 136 with a flex row
- The topbar currently contains: "MSOSync Operations Console" span, then a flex gap-2 row with theme toggle, Avatar, username, logout button
- The connection indicator goes into the topbar flex row, BEFORE the theme toggle button (leftmost control in the right cluster)
- `useSignalRContext()` from `../../shared/signalr/context` returns `{ connectionState, lastConnectedAt, lastDisconnectedAt }`
- `connectionState` values: `'connected'` (hidden — no visual noise), `'reconnecting'` (amber dot + text), `'disconnected'` (red dot + "Offline")
- `cn` utility is already imported in AppLayout from `../../lib/utils`
- Relative import from AppLayout to shared/signalr: `../../shared/signalr/context`

## Interfaces

**Consumes (from Task 2):**
- `useSignalRContext()` from `../../shared/signalr/context`
- `ConnectionState` type from `../../shared/signalr/types`

**Produces:**
- `SignalRIndicator` component — inline in `AppLayout.tsx` (not a separate file; it's a small local component)
- Manual acceptance: all checklist items verified

---

## Files

- Modify: `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`

---

- [ ] **Step 1: Add `SignalRIndicator` to `AppLayout.tsx`**

Open `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`.

**Step 1a:** Add imports. The current imports end at `import { cn } from '../../lib/utils';`. Add after the existing imports:

```tsx
import { useSignalRContext } from '../../shared/signalr/context';
```

**Step 1b:** Add the `SignalRIndicator` component. Add this function BEFORE the `NavGroup` function definition (around line 71):

```tsx
function SignalRIndicator() {
  const { connectionState } = useSignalRContext();

  if (connectionState === 'connected') return null;

  const isReconnecting = connectionState === 'reconnecting';

  return (
    <div
      className={cn(
        'flex items-center gap-1.5 rounded-md px-2 py-1 text-xs font-medium',
        isReconnecting
          ? 'text-amber-600 dark:text-amber-400'
          : 'text-red-600 dark:text-red-400',
      )}
      aria-live="polite"
      aria-label={isReconnecting ? 'Reconnecting to server' : 'Disconnected from server'}
    >
      <span
        className={cn(
          'h-2 w-2 rounded-full shrink-0',
          isReconnecting ? 'bg-amber-500' : 'bg-red-500',
        )}
      />
      {isReconnecting ? 'Reconnecting…' : 'Offline'}
    </div>
  );
}
```

**Step 1c:** Mount the indicator in the topbar. Find the topbar flex row (inside `<header>`):

Current:
```tsx
<div className="flex items-center gap-2">
  <Button variant="ghost" size="icon" onClick={handleThemeToggle} aria-label="Toggle theme">
    {isDark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
  </Button>
  <Avatar className="h-8 w-8">
```

Updated (add `<SignalRIndicator />` before the theme toggle):
```tsx
<div className="flex items-center gap-2">
  <SignalRIndicator />
  <Button variant="ghost" size="icon" onClick={handleThemeToggle} aria-label="Toggle theme">
    {isDark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
  </Button>
  <Avatar className="h-8 w-8">
```

- [ ] **Step 2: Run TypeScript type check**

```pwsh
cd src/MSOSync.Frontend
npx tsc -b --noEmit
```

Expected: no errors. Common issues:
- If `useSignalRContext` import fails: verify file is at `src/shared/signalr/context.ts` and the relative path from `app/layouts/` is `../../shared/signalr/context`
- If `connectionState` type errors: it is typed as `ConnectionState = 'connected' | 'reconnecting' | 'disconnected'` from `types.ts`

- [ ] **Step 3: Run all Vitest tests**

```pwsh
cd src/MSOSync.Frontend
npm run test
```

Expected: all tests pass, no regressions. Count: 27 tests minimum (1 types, 1 useSignalR, 6 eventRouter, 13 notifications, 6 dagre-layout).

- [ ] **Step 4: Run production build**

```pwsh
cd src/MSOSync.Frontend
npm run build
```

Expected: exit 0. If build fails, check TypeScript errors from Step 2 first.

- [ ] **Step 5: Start dev server and open app**

```pwsh
cd src/MSOSync.Frontend
npm run dev
```

Open browser at `http://localhost:5173`. Log in with any valid credentials.

- [ ] **Step 6: Manual acceptance checklist**

Work through the following checks in the browser. Log each item as PASS or FAIL:

```
✓ Login → topbar shows NO indicator (connection is silent when connected)
✓ Logout → topbar clears the indicator (connection stops cleanly)
✓ After login, open DevTools → Network → WS tab → verify /hubs/operations WebSocket connected
✓ Token in WebSocket URL uses ?access_token= param (NOT in Authorization header for WS upgrade)
✓ Navigate to Dashboard → page loads without console errors
✓ Navigate to Topology → React Flow canvas renders
✓ Manually disconnect network (DevTools → Network → Offline) → topbar shows amber dot + "Reconnecting…"
✓ Reconnect network → topbar indicator disappears (back to connected state)
✓ Open 2 browser tabs, both show no indicator after login
✓ Navigate to Metrics page → loads normally (5-min polling now, no 30s flicker)
```

If any manual check fails, fix before committing.

**Note on server-push testing:** Full end-to-end testing of actual push events (node approved → toast appears) requires a running backend server. The manual checklist above focuses on connection lifecycle, which can be verified with just the frontend dev server. Backend push testing is deferred to the integration test described in the spec.

- [ ] **Step 7: Commit**

```pwsh
cd D:\MSOSync
git add src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx
git commit -m "feat(11C): add SignalR connection indicator to app shell"
```

## Report Contract

Return: `DONE`, last commit SHA, `tsc --noEmit` result (clean), test results (count + all pass), build result (exit 0), manual checklist items verified (list PASS/FAIL for each), any concerns. Write full report to the report file path provided by the coordinator.
