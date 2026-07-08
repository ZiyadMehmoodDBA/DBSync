import { render, screen } from '@testing-library/react';
import { TemplatesPage } from '../TemplatesPage';
import { describe, it, expect, vi } from 'vitest';

vi.mock('../hooks', () => ({
  useTemplates: () => ({ data: [], isLoading: false }),
}));
vi.mock('../mutations', () => ({
  useArchiveTemplate: () => ({ mutateAsync: vi.fn(), isPending: false }),
  usePublishTemplate: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock('../../../shared/hooks/usePermissions', () => ({
  useHasPermission: () => true,
}));

describe('TemplatesPage', () => {
  it('renders heading', () => {
    render(<TemplatesPage />);
    expect(screen.getByText('Configuration Templates')).toBeInTheDocument();
  });

  it('shows empty state when no templates', () => {
    render(<TemplatesPage />);
    expect(screen.getByText(/no templates found/i)).toBeInTheDocument();
  });
});
