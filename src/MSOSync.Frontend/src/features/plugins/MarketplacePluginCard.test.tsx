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
