# Task 1: Dependency Migration + Tailwind + shadcn + Vitest Setup

**Part of:** [Epic 10A Plan](2026-06-29-epic10a-react-foundation.md)

**Goal:** Strip MUI/emotion/zustand/recharts/react-force-graph from the existing Vite scaffold, install Tailwind 4 + shadcn/ui + Vitest + remaining runtime deps, configure dev proxy, wire dark-mode init, and verify clean build.

**Files:**
- Modify: `src/MSOSync.Frontend/package.json`
- Modify: `src/MSOSync.Frontend/vite.config.ts`
- Modify: `src/MSOSync.Frontend/src/index.css`
- Modify: `src/MSOSync.Frontend/src/main.tsx`
- Modify: `src/MSOSync.Frontend/src/App.tsx`
- Create: `src/MSOSync.Frontend/vitest.config.ts`
- Create: `src/MSOSync.Frontend/src/test-setup.ts`
- Create (shadcn-generated): `src/MSOSync.Frontend/components.json`, `src/MSOSync.Frontend/src/lib/utils.ts`, `src/MSOSync.Frontend/src/components/ui/*.tsx`

**Interfaces:**
- Produces:
  - `src/MSOSync.Frontend/src/lib/utils.ts` exports `cn(...inputs: ClassValue[]): string` (shadcn utility)
  - `vitest.config.ts` exports Vitest config with `jsdom` environment
  - `vite.config.ts` proxies `/api` → `http://localhost:5000`
  - `main.tsx` reads `msosync.theme` from localStorage and sets `html.dark` class before mount

---

All commands below run from `src/MSOSync.Frontend/` unless stated otherwise.

- [ ] **Step 1: Remove old dependencies**

```powershell
cd src/MSOSync.Frontend
npm uninstall @mui/material @emotion/react @emotion/styled recharts react-force-graph zustand
```

Expected: package.json no longer lists those packages.

- [ ] **Step 2: Install Tailwind 4 and Vite plugin**

```powershell
npm install tailwindcss@^4 @tailwindcss/vite
```

Expected: `tailwindcss` and `@tailwindcss/vite` appear in `package.json` dependencies.

- [ ] **Step 3: Install runtime dependencies**

```powershell
npm install lucide-react zod react-hook-form @hookform/resolvers @xyflow/react apexcharts react-apexcharts
```

Expected: all packages appear in `package.json` dependencies.

- [ ] **Step 4: Install Vitest and Testing Library**

```powershell
npm install -D vitest @vitest/coverage-v8 @testing-library/react @testing-library/user-event jsdom
```

Expected: packages appear in `devDependencies`.

- [ ] **Step 5: Update `vite.config.ts`**

Replace the entire file:

```ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
});
```

- [ ] **Step 6: Update `src/index.css`**

Replace the entire file:

```css
@import "tailwindcss";

@variant dark (&:is(.dark *));
```

The `@variant dark` line enables class-based dark mode (`.dark` on `<html>` activates `dark:` utilities).

- [ ] **Step 7: Update `src/main.tsx` with theme initialization**

Replace the entire file:

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import App from './App.tsx';

// Prevent flash of incorrect theme — run before React mounts
const savedTheme = localStorage.getItem('msosync.theme') ?? 'light';
document.documentElement.classList.toggle('dark', savedTheme === 'dark');

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
```

- [ ] **Step 8: Simplify `src/App.tsx`**

Replace the entire file with a minimal stub (router wiring comes in Task 3):

```tsx
export default function App() {
  return (
    <div className="min-h-screen bg-white dark:bg-neutral-950 text-neutral-900 dark:text-neutral-100 flex items-center justify-center">
      <p className="text-2xl font-semibold">MSOSync — Loading…</p>
    </div>
  );
}
```

This verifies Tailwind classes work before the full router is wired.

- [ ] **Step 9: Create `vitest.config.ts`**

```ts
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test-setup.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov'],
    },
  },
});
```

- [ ] **Step 10: Create `src/test-setup.ts`**

```ts
import '@testing-library/jest-dom';
```

Note: `@testing-library/jest-dom` is included transitively via `@testing-library/react`. If the import fails, run `npm install -D @testing-library/jest-dom`.

- [ ] **Step 11: Add test script to `package.json`**

Open `package.json`. In the `"scripts"` section, add:

```json
"test": "vitest run",
"test:watch": "vitest",
"test:coverage": "vitest run --coverage"
```

- [ ] **Step 12: Initialize shadcn/ui**

```powershell
npx shadcn@latest init
```

When prompted:
- Style: **Default**
- Base color: **Neutral**
- CSS variables: **Yes**

This creates `components.json` and `src/lib/utils.ts`. Do NOT commit `node_modules`.

- [ ] **Step 13: Install shadcn components**

```powershell
npx shadcn@latest add button card input label separator avatar badge skeleton
```

Expected: `src/components/ui/` directory with 8 component files. Each is a `.tsx` file committed to the repo.

- [ ] **Step 14: Verify dev build**

```powershell
npm run dev
```

Open `http://localhost:5173`. Expected: white/dark page with "MSOSync — Loading…" text. No console errors. No TypeScript errors in the terminal.

- [ ] **Step 15: Verify production build**

```powershell
npm run build
```

Expected: `dist/` directory created with `index.html` + hashed assets. Exit code 0.

- [ ] **Step 16: Verify lint**

```powershell
npm run lint
```

Expected: 0 errors. Fix any ESLint errors (likely `react-refresh/only-export-components` for the App stub — acceptable at this stage since full router wiring is Task 3).

- [ ] **Step 17: Verify Vitest runs**

```powershell
npm test
```

Expected: "No test files found" or 0 tests run (test files added in Task 2). Exit code 0.

- [ ] **Step 18: Commit**

```powershell
git add src/MSOSync.Frontend/package.json
git add src/MSOSync.Frontend/package-lock.json
git add src/MSOSync.Frontend/vite.config.ts
git add src/MSOSync.Frontend/vitest.config.ts
git add src/MSOSync.Frontend/components.json
git add src/MSOSync.Frontend/src/index.css
git add src/MSOSync.Frontend/src/main.tsx
git add src/MSOSync.Frontend/src/App.tsx
git add src/MSOSync.Frontend/src/test-setup.ts
git add src/MSOSync.Frontend/src/lib/
git add src/MSOSync.Frontend/src/components/
git commit -m "feat(10a): migrate deps, init Tailwind 4, shadcn/ui, Vitest"
```
