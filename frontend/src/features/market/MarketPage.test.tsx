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
    pricePerKg: 6.4, quantityAvailableKg: 3, note: null, createdAt: new Date().toISOString(),
    aiTastingNotes: null, hasPhoto: false,
  },
  {
    id: 'l2', sellerId: 'seller-2', berryType: 'Blueberries', farmName: 'Blue Hollow Orchard',
    pricePerKg: 5.2, quantityAvailableKg: 0, note: null, createdAt: new Date().toISOString(),
    aiTastingNotes: null, hasPhoto: false,
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
    // intermediate "2.5 kg left" state is never actually painted. A small real delay gives
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
    // Default picker quantity is 0.5 kg (see MarketPage's quantityFor).
    await user.click(within(strawberryCard).getByRole('button', { name: 'Buy 0.50 kg' }));

    await waitFor(() => expect(within(strawberryCard).getByText('2.5 kg left')).toBeInTheDocument());
    await waitFor(() => expect(within(strawberryCard).getByText('3 kg left')).toBeInTheDocument());
    expect(await screen.findByText('Sold out.')).toBeInTheDocument();
  });

  it('runs smart search and shows the semantic results with a mode badge', async () => {
    vi.mocked(listingsApi.getListings).mockResolvedValue(listings);
    vi.mocked(listingsApi.searchListings).mockResolvedValue({
      mode: 'semantic',
      results: [
        {
          id: 'l3', sellerId: 'seller-3', berryType: 'Strawberry', farmName: 'Sweet Fields',
          pricePerKg: 7.0, quantityAvailableKg: 4, note: null, createdAt: new Date().toISOString(),
          aiTastingNotes: 'Candy-sweet.', hasPhoto: false,
        },
      ],
    });
    const user = userEvent.setup();

    renderWithProviders(<MarketPage />, { route: '/market' });

    await screen.findByRole('heading', { name: 'Strawberries' });
    await user.type(screen.getByRole('searchbox'), 'sweet berries for jam');
    await user.click(screen.getByRole('button', { name: /smart search/i }));

    expect(await screen.findByText(/smart results · semantic/i)).toBeInTheDocument();
    expect(screen.getByText('Sweet Fields')).toBeInTheDocument();
    expect(screen.getByText('Candy-sweet.')).toBeInTheDocument();
  });

  it('shows a distinct error message when the market fails to load, not "no results"', async () => {
    vi.mocked(listingsApi.getListings).mockRejectedValue(new ApiError(0, ['Network error']));

    renderWithProviders(<MarketPage />, { route: '/market' });

    expect(await screen.findByText("Couldn't load the market — check your connection and try again.")).toBeInTheDocument();
    expect(screen.queryByText('No crates match that search.')).not.toBeInTheDocument();
  });

  it('shows a toast instead of an unhandled rejection when smart search fails', async () => {
    vi.mocked(listingsApi.getListings).mockResolvedValue(listings);
    vi.mocked(listingsApi.searchListings).mockRejectedValue(new ApiError(500, ['boom']));
    const user = userEvent.setup();

    renderWithProviders(<MarketPage />, { route: '/market' });

    await screen.findByRole('heading', { name: 'Strawberries' });
    await user.type(screen.getByRole('searchbox'), 'sweet berries for jam');
    await user.click(screen.getByRole('button', { name: /smart search/i }));

    expect(await screen.findByText('Smart search failed — try again.')).toBeInTheDocument();
  });
});
