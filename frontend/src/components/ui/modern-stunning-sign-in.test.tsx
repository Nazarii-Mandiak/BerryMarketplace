import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../testUtils';
import { SignIn1 } from './modern-stunning-sign-in';
import * as accountsApi from '../../api/accounts';
import { ApiError } from '../../api/client';

vi.mock('../../api/accounts');
vi.mock('@react-oauth/google', () => ({
  GoogleLogin: ({ onSuccess }: { onSuccess: (response: { credential: string }) => void }) => (
    <button type="button" onClick={() => onSuccess({ credential: 'fake-id-token' })}>
      Continue with Google (mock)
    </button>
  ),
}));

describe('SignIn1 Google sign-in', () => {
  beforeEach(() => {
    vi.stubEnv('VITE_GOOGLE_CLIENT_ID', 'test-client-id');
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('signs in with Google when the credential succeeds', async () => {
    vi.mocked(accountsApi.loginWithGoogle).mockResolvedValue({
      id: 'user-1', email: 'buyer@example.com', displayName: 'Buyer',
    });
    const user = userEvent.setup();

    renderWithProviders(<SignIn1 />, { route: '/login' });

    await user.click(screen.getByRole('button', { name: 'Continue with Google (mock)' }));

    await waitFor(() =>
      expect(accountsApi.loginWithGoogle).toHaveBeenCalledWith({ credential: 'fake-id-token' }),
    );
  });

  it('shows an error message when Google sign-in fails', async () => {
    vi.mocked(accountsApi.loginWithGoogle).mockRejectedValue(new ApiError(401, []));
    const user = userEvent.setup();

    renderWithProviders(<SignIn1 />, { route: '/login' });

    await user.click(screen.getByRole('button', { name: 'Continue with Google (mock)' }));

    expect(await screen.findByText('Google sign-in failed — try again.')).toBeInTheDocument();
  });

  it('does not render the Google button when no client id is configured', () => {
    vi.stubEnv('VITE_GOOGLE_CLIENT_ID', '');

    renderWithProviders(<SignIn1 />, { route: '/login' });

    expect(screen.queryByRole('button', { name: 'Continue with Google (mock)' })).not.toBeInTheDocument();
  });
});
