import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../testUtils';
import { MarketPage } from './MarketPage';
import * as listingsApi from '../../api/listings';
import * as accountsApi from '../../api/accounts';
import { ApiError } from '../../api/client';
import type { ListingResponse } from '../../api/types';

vi.mock('../../api/listings');
vi.mock('../../api/accounts');

const listings: ListingResponse[] = [
  {
    id: 'l1', sellerId: 'seller-1', berryType: 'Strawberries', farmName: 'Sunrow Farm',
    pricePerPint: 6.4, quantityAvailable: 3, note: null, createdAt: new Date().toISOString(),
  },
  {
    id: 'l2', sellerId: 'seller-2', berryType: 'Blueberries', farmName: 'Blue Hollow Orchard',
    pricePerPint: 5.2, quantityAvailable: 0, note: null, createdAt: new Date().toISOString(),
  },
];

beforeEach(() => {
  vi.mocked(accountsApi.getMe).mockRejectedValue(new ApiError(401, []));
});

describe('MarketPage', () => {
  it('filters listings by berry-type chip', async () => {
    vi.mocked(listingsApi.getListings).mockResolvedValue(listings);
    const user = userEvent.setup();

    renderWithProviders(<MarketPage />, { route: '/market' });

    await screen.findByRole('heading', { name: 'Strawberries' });
    await user.click(screen.getByRole('button', { name: 'Blueberries' }));

    expect(screen.queryByRole('heading', { name: 'Strawberries' })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Blueberries' })).toBeInTheDocument();
  });

  it('disables buying a sold-out listing', async () => {
    vi.mocked(listingsApi.getListings).mockResolvedValue(listings);

    renderWithProviders(<MarketPage />, { route: '/market' });

    const blueberryCard = (await screen.findByRole('heading', { name: 'Blueberries' })).closest(
      '.card',
    ) as HTMLElement;
    expect(within(blueberryCard).getByRole('button', { name: 'Sold out' })).toBeDisabled();
  });

  it('optimistically decrements quantity on buy, then rolls back on 409', async () => {
    vi.mocked(listingsApi.getListings).mockResolvedValue(listings);
    // A same-tick rejection (mockRejectedValue) settles in the same microtask flush as the
    // optimistic onMutate update, so React 19 batches both into a single commit and the
    // intermediate "2 pts left" state is never actually painted. A small real delay gives
    // the optimistic render its own commit before the rollback, which is what this test
    // is meant to observe.
    vi.mocked(listingsApi.reserveListing).mockImplementation(
      () => new Promise((_resolve, reject) => setTimeout(() => reject(new ApiError(409, ['Sold out.'])), 10)),
    );
    const user = userEvent.setup();

    renderWithProviders(<MarketPage />, { route: '/market' });

    const strawberryCard = (await screen.findByRole('heading', { name: 'Strawberries' })).closest(
      '.card',
    ) as HTMLElement;
    await user.click(within(strawberryCard).getByRole('button', { name: 'Buy a pint' }));

    await waitFor(() => expect(within(strawberryCard).getByText('2 pts left')).toBeInTheDocument());
    await waitFor(() => expect(within(strawberryCard).getByText('3 pts left')).toBeInTheDocument());
    expect(await screen.findByText('Sold out.')).toBeInTheDocument();
  });
});
