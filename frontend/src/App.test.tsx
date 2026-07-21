import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from './testUtils';
import { App } from './App';
import * as accountsApi from './api/accounts';
import * as listingsApi from './api/listings';
import { ApiError } from './api/client';

vi.mock('./api/accounts');
vi.mock('./api/listings');

beforeEach(() => {
  vi.mocked(accountsApi.getMe).mockRejectedValue(new ApiError(401, []));
  vi.mocked(listingsApi.getListings).mockResolvedValue([]);
});

describe('App', () => {
  it('redirects the index route to the market and shows a logged-out header', async () => {
    renderWithProviders(<App />, { route: '/' });

    expect(await screen.findByText('Berries, straight from the row.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Log in' })).toBeInTheDocument();
  });

  it('redirects an unauthenticated visitor away from /sell to /login', async () => {
    renderWithProviders(<App />, { route: '/sell' });

    expect(await screen.findByRole('heading', { name: 'Log in' })).toBeInTheDocument();
  });
});
