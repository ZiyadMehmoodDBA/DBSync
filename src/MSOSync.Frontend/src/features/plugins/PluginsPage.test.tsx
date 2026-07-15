import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';
import { PluginsPage } from './PluginsPage';

vi.mock('./hooks', () => ({
  usePlugins:       () => ({ data: [], isLoading: false, isError: false, error: null }),
  usePluginSummary: () => ({ data: null, isLoading: false }),
  useEnablePlugin:  () => ({ mutate: vi.fn() }),
  useDisablePlugin: () => ({ mutate: vi.fn() }),
}));

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe('PluginsPage', () => {
  it('renders empty state when no plugins', () => {
    render(<PluginsPage />, { wrapper });
    expect(screen.getByText(/no plugins discovered/i)).toBeInTheDocument();
  });

  it('renders page title', () => {
    render(<PluginsPage />, { wrapper });
    expect(screen.getByText('Plugins')).toBeInTheDocument();
  });
});
