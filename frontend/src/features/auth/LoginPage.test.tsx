import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../testUtils';
import { LoginPage } from './LoginPage';
import * as accountsApi from '../../api/accounts';
import { ApiError } from '../../api/client';

vi.mock('../../api/accounts');

describe('LoginPage', () => {
  it('logs in and shows a friendly message on invalid credentials', async () => {
    vi.mocked(accountsApi.login).mockRejectedValue(new ApiError(401, []));
    const user = userEvent.setup();

    renderWithProviders(<LoginPage />, { route: '/login' });

    await user.type(screen.getByLabelText('Email'), 'buyer@example.com');
    await user.type(screen.getByLabelText('Password'), 'wrong-password');
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    expect(await screen.findByText('Invalid email or password.')).toBeInTheDocument();
  });

  it('submits credentials to the login API', async () => {
    vi.mocked(accountsApi.login).mockResolvedValue({
      id: 'user-1', email: 'buyer@example.com', displayName: 'Buyer',
    });
    const user = userEvent.setup();

    renderWithProviders(<LoginPage />, { route: '/login' });

    await user.type(screen.getByLabelText('Email'), 'buyer@example.com');
    await user.type(screen.getByLabelText('Password'), 'Password123!');
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    await waitFor(() =>
      expect(accountsApi.login).toHaveBeenCalledWith({ email: 'buyer@example.com', password: 'Password123!' }),
    );
  });
});
