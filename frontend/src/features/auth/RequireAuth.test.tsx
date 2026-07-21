import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { Route, Routes } from 'react-router-dom';
import { renderWithProviders } from '../../testUtils';
import { RequireAuth } from './RequireAuth';
import * as accountsApi from '../../api/accounts';
import { ApiError } from '../../api/client';

vi.mock('../../api/accounts');

function TestRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<p>Login page</p>} />
      <Route element={<RequireAuth />}>
        <Route path="/sell" element={<p>Sell page</p>} />
      </Route>
    </Routes>
  );
}

describe('RequireAuth', () => {
  it('redirects to /login when there is no signed-in user', async () => {
    vi.mocked(accountsApi.getMe).mockRejectedValue(new ApiError(401, []));

    renderWithProviders(<TestRoutes />, { route: '/sell' });

    expect(await screen.findByText('Login page')).toBeInTheDocument();
  });

  it('renders the protected route when a user is signed in', async () => {
    vi.mocked(accountsApi.getMe).mockResolvedValue({
      id: 'user-1', email: 'buyer@example.com', displayName: 'Buyer',
    });

    renderWithProviders(<TestRoutes />, { route: '/sell' });

    expect(await screen.findByText('Sell page')).toBeInTheDocument();
  });
});
