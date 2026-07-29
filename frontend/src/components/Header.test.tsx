import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../testUtils';
import { Header } from './Header';
import * as accountsApi from '../api/accounts';
import { ApiError } from '../api/client';
import type { UserResponse } from '../api/types';

vi.mock('../api/accounts');

const user: UserResponse = { id: 'u1', email: 'grower@example.com', displayName: 'Grower Gail' };

beforeEach(() => {
  vi.mocked(accountsApi.getMe).mockResolvedValue(user);
});

describe('Header', () => {
  it('logging out clears the current user without requiring a refresh', async () => {
    // Mirrors the real flow: the server has already cleared the auth cookie by the time
    // logout resolves, so the next getMe() call (triggered by queryClient.clear()) 401s.
    vi.mocked(accountsApi.logout).mockImplementation(async () => {
      vi.mocked(accountsApi.getMe).mockRejectedValue(new ApiError(401, []));
    });
    const u = userEvent.setup();

    renderWithProviders(<Header />, { route: '/market' });

    await screen.findByText('Grower Gail');
    await u.click(screen.getByRole('button', { name: 'Log out' }));

    await waitFor(() => expect(screen.getByRole('link', { name: 'Log in' })).toBeInTheDocument());
    expect(screen.queryByText('Grower Gail')).not.toBeInTheDocument();
  });

  it('shows a toast instead of failing silently when logout errors', async () => {
    vi.mocked(accountsApi.logout).mockRejectedValue(new ApiError(500, ['boom']));
    const u = userEvent.setup();

    renderWithProviders(<Header />, { route: '/market' });

    await screen.findByText('Grower Gail');
    await u.click(screen.getByRole('button', { name: 'Log out' }));

    expect(await screen.findByText('Log out failed — try again.')).toBeInTheDocument();
    expect(screen.getByText('Grower Gail')).toBeInTheDocument();
  });
});
