# Task 3: MarketplaceStarRating + MarketplacePluginCard + Card Tests

> Part of the [Phase 2C.3 master plan](./2026-07-23-phase-2C-3-master.md)

**Prerequisite:** Task 1 complete — types must exist. Task 2 not required for rendering tests (hooks are mocked).

**Goal:** Build the two leaf components (`MarketplaceStarRating` and `MarketplacePluginCard`) and verify them with component tests. These are consumed by `MarketplacePage` in Task 4.

**Files:**
- Create: `src/MSOSync.Frontend/src/features/plugins/MarketplaceStarRating.tsx`
- Create: `src/MSOSync.Frontend/src/features/plugins/MarketplacePluginCard.tsx`
- Create: `src/MSOSync.Frontend/src/features/plugins/MarketplacePluginCard.test.tsx`

**Interfaces:**
- Consumes: `MarketplacePluginListItemDto` from `shared/types/marketplace`
- Produces:
  - `MarketplaceStarRating({ rating, ratingCount, showCount? })` — consumed by `MarketplacePluginCard`
  - `MarketplacePluginCard({ plugin, isInstalled, onSelect, onInstall, isInstalling })` — consumed by `MarketplacePage`

---

- [ ] **Step 1: Create `MarketplaceStarRating.tsx`**

Create `src/MSOSync.Frontend/src/features/plugins/MarketplaceStarRating.tsx`:

```typescript
import { Star } from 'lucide-react';
import { cn } from '../../lib/utils';

interface MarketplaceStarRatingProps {
  rating:      number;   // 0.0–5.0
  ratingCount: number;
  showCount?:  boolean;  // default true
}

export function MarketplaceStarRating({
  rating,
  ratingCount,
  showCount = true,
}: MarketplaceStarRatingProps) {
  const fullStars = Math.floor(rating);
  const fractional = rating - fullStars;

  return (
    <span
      className="flex items-center gap-0.5"
      aria-label={`Rated ${rating} out of 5`}
    >
      {Array.from({ length: 5 }, (_, i) => {
        const isFull    = i < fullStars;
        const isPartial = i === fullStars && fractional > 0;
        return (
          <Star
            key={i}
            className={cn(
              'h-3 w-3',
              isFull
                ? 'fill-amber-400 text-amber-400'
                : isPartial
                  ? 'fill-amber-400 text-amber-400'
                  : 'fill-none text-neutral-300 dark:text-neutral-600',
            )}
            style={isPartial ? { opacity: 0.3 + fractional * 0.7 } : undefined}
          />
        );
      })}
      {showCount && (
        <span className="ml-1 text-xs text-neutral-500 dark:text-neutral-400">
          ({ratingCount})
        </span>
      )}
    </span>
  );
}
```

- [ ] **Step 2: Create `MarketplacePluginCard.tsx`**

Create `src/MSOSync.Frontend/src/features/plugins/MarketplacePluginCard.tsx`:

```typescript
import { useState } from 'react';
import { Package, ShieldCheck, Loader2 } from 'lucide-react';
import { Button } from '../../components/ui/button';
import { Badge } from '../../components/ui/badge';
import { MarketplaceStarRating } from './MarketplaceStarRating';
import type { MarketplacePluginListItemDto } from '../../shared/types/marketplace';

interface MarketplacePluginCardProps {
  plugin:       MarketplacePluginListItemDto;
  isInstalled:  boolean;
  onSelect:     (id: string) => void;
  onInstall:    (id: string, name: string) => void;
  isInstalling: boolean;
}

export function MarketplacePluginCard({
  plugin,
  isInstalled,
  onSelect,
  onInstall,
  isInstalling,
}: MarketplacePluginCardProps) {
  const [iconError, setIconError] = useState(false);
  const showIcon = plugin.iconUrl && !iconError;

  const downloadLabel = new Intl.NumberFormat('en-US', {
    notation: 'compact',
    maximumFractionDigits: 1,
  }).format(plugin.downloadCount);

  return (
    <div
      className="relative flex flex-col gap-3 rounded-lg border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 p-4 hover:border-neutral-300 dark:hover:border-neutral-600 transition-colors cursor-pointer"
      onClick={() => onSelect(plugin.id)}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') onSelect(plugin.id); }}
    >
      {/* Header row */}
      <div className="flex items-start gap-3">
        <div className="shrink-0">
          {showIcon ? (
            <img
              src={plugin.iconUrl!}
              alt={`${plugin.name} icon`}
              className="h-8 w-8 rounded"
              onError={() => setIconError(true)}
            />
          ) : (
            <Package className="h-8 w-8 text-neutral-400" />
          )}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5">
            <span className="font-medium text-neutral-900 dark:text-neutral-100 truncate">
              {plugin.name}
            </span>
            {plugin.verified && (
              <ShieldCheck
                className="h-4 w-4 shrink-0 text-blue-500"
                aria-label="Verified publisher"
              />
            )}
          </div>
          <p className="text-xs text-neutral-500 dark:text-neutral-400">{plugin.author}</p>
        </div>
        <span className="rounded-full px-2 py-0.5 text-xs bg-neutral-100 dark:bg-neutral-800 text-neutral-600 dark:text-neutral-300 shrink-0">
          {plugin.category}
        </span>
      </div>

      {/* Description */}
      <p className="line-clamp-2 text-sm text-neutral-600 dark:text-neutral-400">
        {plugin.description}
      </p>

      {/* Footer row */}
      <div className="flex items-center justify-between mt-auto">
        <div className="flex items-center gap-3">
          <MarketplaceStarRating rating={plugin.rating} ratingCount={plugin.ratingCount} />
          <span className="text-xs text-neutral-500">{downloadLabel} downloads</span>
          <span className="text-xs text-neutral-400 font-mono">v{plugin.latestVersion}</span>
        </div>

        {/* Action — stop click propagation so card body click doesn't also fire */}
        <div onClick={(e) => e.stopPropagation()}>
          {isInstalled ? (
            <Badge variant="secondary" className="bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400">
              Installed
            </Badge>
          ) : (
            <Button
              size="sm"
              disabled={isInstalling}
              onClick={() => onInstall(plugin.id, plugin.name)}
              aria-label={`Install ${plugin.name}`}
            >
              {isInstalling ? (
                <Loader2 className="h-3 w-3 animate-spin mr-1" />
              ) : null}
              Install
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Write the failing tests for `MarketplacePluginCard`**

Create `src/MSOSync.Frontend/src/features/plugins/MarketplacePluginCard.test.tsx`:

```typescript
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MarketplacePluginCard } from './MarketplacePluginCard';
import type { MarketplacePluginListItemDto } from '../../shared/types/marketplace';

const basePlugin: MarketplacePluginListItemDto = {
  id:             'com.example.myplugin',
  name:           'My Plugin',
  author:         'Example Corp',
  description:    'A test plugin for demonstration purposes.',
  category:       'Collector',
  tags:           ['test'],
  latestVersion:  '1.2.3',
  minHostVersion: '9.0.0',
  downloadCount:  12400,
  rating:         4.3,
  ratingCount:    87,
  publishedAt:    '2026-01-01T00:00:00Z',
  updatedAt:      '2026-06-01T00:00:00Z',
  iconUrl:        null,
  verified:       false,
};

describe('MarketplacePluginCard', () => {
  it('renders plugin name and author', () => {
    render(
      <MarketplacePluginCard
        plugin={basePlugin}
        isInstalled={false}
        onSelect={vi.fn()}
        onInstall={vi.fn()}
        isInstalling={false}
      />,
    );
    expect(screen.getByText('My Plugin')).toBeInTheDocument();
    expect(screen.getByText('Example Corp')).toBeInTheDocument();
  });

  it('renders Installed badge when isInstalled', () => {
    render(
      <MarketplacePluginCard
        plugin={basePlugin}
        isInstalled={true}
        onSelect={vi.fn()}
        onInstall={vi.fn()}
        isInstalling={false}
      />,
    );
    expect(screen.getByText('Installed')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /install my plugin/i })).not.toBeInTheDocument();
  });

  it('renders Install button when not installed', () => {
    render(
      <MarketplacePluginCard
        plugin={basePlugin}
        isInstalled={false}
        onSelect={vi.fn()}
        onInstall={vi.fn()}
        isInstalling={false}
      />,
    );
    expect(screen.getByRole('button', { name: /install my plugin/i })).toBeInTheDocument();
    expect(screen.queryByText('Installed')).not.toBeInTheDocument();
  });

  it('renders loading spinner when isInstalling', () => {
    render(
      <MarketplacePluginCard
        plugin={basePlugin}
        isInstalled={false}
        onSelect={vi.fn()}
        onInstall={vi.fn()}
        isInstalling={true}
      />,
    );
    const btn = screen.getByRole('button', { name: /install my plugin/i });
    expect(btn).toBeDisabled();
    // Loader2 renders as an SVG with animate-spin class
    expect(btn.querySelector('.animate-spin')).not.toBeNull();
  });

  it('renders verified badge for verified plugins', () => {
    render(
      <MarketplacePluginCard
        plugin={{ ...basePlugin, verified: true }}
        isInstalled={false}
        onSelect={vi.fn()}
        onInstall={vi.fn()}
        isInstalling={false}
      />,
    );
    expect(screen.getByLabelText('Verified publisher')).toBeInTheDocument();
  });

  it('renders Package fallback icon when iconUrl is null', () => {
    render(
      <MarketplacePluginCard
        plugin={{ ...basePlugin, iconUrl: null }}
        isInstalled={false}
        onSelect={vi.fn()}
        onInstall={vi.fn()}
        isInstalling={false}
      />,
    );
    // MarketplaceStarRating renders Package icon — check that no <img> is present
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('calls onInstall with plugin id and name when Install button clicked', async () => {
    const onInstall = vi.fn();
    render(
      <MarketplacePluginCard
        plugin={basePlugin}
        isInstalled={false}
        onSelect={vi.fn()}
        onInstall={onInstall}
        isInstalling={false}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: /install my plugin/i }));
    expect(onInstall).toHaveBeenCalledWith('com.example.myplugin', 'My Plugin');
  });

  it('calls onSelect with plugin id when card body clicked', async () => {
    const onSelect = vi.fn();
    render(
      <MarketplacePluginCard
        plugin={basePlugin}
        isInstalled={false}
        onSelect={onSelect}
        onInstall={vi.fn()}
        isInstalling={false}
      />,
    );
    // Click the description text (part of card body, not the Install button)
    await userEvent.click(screen.getByText('A test plugin for demonstration purposes.'));
    expect(onSelect).toHaveBeenCalledWith('com.example.myplugin');
  });
});
```

- [ ] **Step 4: Run card tests to verify they fail (component not yet wired)**

```bash
cd src/MSOSync.Frontend && npm test -- --testPathPattern=MarketplacePluginCard
```

Expected: all tests pass because the component is already created. If they fail, diagnose and fix before proceeding.

- [ ] **Step 5: Run card tests to confirm all pass**

```bash
cd src/MSOSync.Frontend && npm test -- --testPathPattern=MarketplacePluginCard
```

Expected: 7 tests pass, 0 failures.

- [ ] **Step 6: Commit**

```bash
git add src/MSOSync.Frontend/src/features/plugins/MarketplaceStarRating.tsx \
        src/MSOSync.Frontend/src/features/plugins/MarketplacePluginCard.tsx \
        src/MSOSync.Frontend/src/features/plugins/MarketplacePluginCard.test.tsx
git commit -m "feat(2C.3-T3): add MarketplaceStarRating, MarketplacePluginCard + tests"
```
