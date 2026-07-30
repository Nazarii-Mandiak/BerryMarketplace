import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router-dom';
import { renderWithProviders } from '../../testUtils';
import { SellPage } from './SellPage';
import * as listingsApi from '../../api/listings';
import * as aiApi from '../../api/ai';
import { ApiError } from '../../api/client';
import type { ListingResponse } from '../../api/types';

vi.mock('../../api/listings');
vi.mock('../../api/ai');

const baseListing: ListingResponse = {
  id: 'listing-1', sellerId: 'user-1', berryType: 'Tayberries', farmName: 'Sunrow Farm',
  pricePerKg: 6.4, quantityAvailableKg: 10, note: null, createdAt: new Date().toISOString(),
  aiTastingNotes: null, hasPhoto: false,
};

describe('SellPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(aiApi.getAiStatus).mockResolvedValue({ enabled: false });
  });

  it('shows backend validation errors inline', async () => {
    vi.mocked(listingsApi.createListing).mockRejectedValue(new ApiError(400, ['BerryType is required.']));
    const user = userEvent.setup();

    renderWithProviders(<SellPage />, { route: '/sell' });

    await user.type(screen.getByLabelText('Farm or garden'), 'Sunrow Farm');
    await user.type(screen.getByLabelText('Price per kg ($)'), '6.40');
    await user.type(screen.getByLabelText('Kilograms available'), '10');

    const form = screen.getByRole('button', { name: 'Post listing' }).closest('form')!;
    fireEvent.submit(form);

    expect(await screen.findByText('BerryType is required.')).toBeInTheDocument();
  });

  it('submits a new listing with the entered fields', async () => {
    vi.mocked(listingsApi.createListing).mockResolvedValue(baseListing);
    const user = userEvent.setup();

    renderWithProviders(<SellPage />, { route: '/sell' });

    await user.type(screen.getByLabelText('Berry'), 'Tayberries');
    await user.type(screen.getByLabelText('Farm or garden'), 'Sunrow Farm');
    await user.type(screen.getByLabelText('Price per kg ($)'), '6.40');
    await user.type(screen.getByLabelText('Kilograms available'), '10');
    await user.click(screen.getByRole('button', { name: 'Post listing' }));

    await waitFor(() =>
      expect(listingsApi.createListing).toHaveBeenCalledWith({
        berryType: 'Tayberries', farmName: 'Sunrow Farm', pricePerKg: 6.4, quantityAvailableKg: 10, note: null,
      }),
    );
  });

  it('uploads the chosen photo after the listing is created', async () => {
    vi.mocked(listingsApi.createListing).mockResolvedValue(baseListing);
    vi.mocked(listingsApi.uploadListingPhoto).mockResolvedValue(undefined);
    const user = userEvent.setup();
    const file = new File(['fake image bytes'], 'berry.png', { type: 'image/png' });

    renderWithProviders(<SellPage />, { route: '/sell' });

    await user.type(screen.getByLabelText('Berry'), 'Tayberries');
    await user.type(screen.getByLabelText('Farm or garden'), 'Sunrow Farm');
    await user.type(screen.getByLabelText('Price per kg ($)'), '6.40');
    await user.type(screen.getByLabelText('Kilograms available'), '10');
    await user.upload(screen.getByLabelText('Photo (optional)'), file);
    await user.click(screen.getByRole('button', { name: 'Post listing' }));

    await waitFor(() => expect(listingsApi.uploadListingPhoto).toHaveBeenCalledWith('listing-1', file));
  });

  it('still saves the listing when the photo upload fails, and toasts instead of blocking', async () => {
    vi.mocked(listingsApi.createListing).mockResolvedValue(baseListing);
    vi.mocked(listingsApi.uploadListingPhoto).mockRejectedValue(new ApiError(400, ['bad image']));
    const user = userEvent.setup();
    const file = new File(['fake image bytes'], 'berry.png', { type: 'image/png' });

    renderWithProviders(<SellPage />, { route: '/sell' });

    await user.type(screen.getByLabelText('Berry'), 'Tayberries');
    await user.type(screen.getByLabelText('Farm or garden'), 'Sunrow Farm');
    await user.type(screen.getByLabelText('Price per kg ($)'), '6.40');
    await user.type(screen.getByLabelText('Kilograms available'), '10');
    await user.upload(screen.getByLabelText('Photo (optional)'), file);
    await user.click(screen.getByRole('button', { name: 'Post listing' }));

    expect(await screen.findByText(/photo failed to upload/i)).toBeInTheDocument();
    expect(listingsApi.createListing).toHaveBeenCalled();
  });

  it('fills the form from the AI suggestion', async () => {
    vi.mocked(aiApi.getAiStatus).mockResolvedValue({ enabled: true });
    vi.mocked(aiApi.suggestListing).mockResolvedValue({
      improvedDescription: 'Sun-ripened and jam-ready',
      suggestedPricePerKg: 6.25,
      reasoning: 'Comparable strawberries sell for $5.50-$7.00.',
    });
    const user = userEvent.setup();

    renderWithProviders(<SellPage />, { route: '/sell' });

    await user.type(screen.getByLabelText('Berry'), 'Strawberry');
    await user.type(screen.getByLabelText('Farm or garden'), 'My Farm');
    await user.click(await screen.findByRole('button', { name: /improve with ai/i }));

    expect(await screen.findByDisplayValue('Sun-ripened and jam-ready')).toBeInTheDocument();
    expect(screen.getByDisplayValue('6.25')).toBeInTheDocument();
    expect(screen.getByText(/comparable strawberries/i)).toBeInTheDocument();
  });

  it('prefills from the existing listing and submits an update when editing', async () => {
    vi.mocked(listingsApi.getListing).mockResolvedValue(baseListing);
    vi.mocked(listingsApi.updateListing).mockResolvedValue(baseListing);

    renderWithProviders(
      <Routes>
        <Route path="/sell/:id" element={<SellPage />} />
      </Routes>,
      { route: '/sell/listing-1' },
    );

    expect(await screen.findByDisplayValue('Tayberries')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Sunrow Farm')).toBeInTheDocument();
    expect(screen.getByDisplayValue('6.4')).toBeInTheDocument();
    expect(screen.getByDisplayValue('10')).toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() =>
      expect(listingsApi.updateListing).toHaveBeenCalledWith('listing-1', {
        berryType: 'Tayberries', farmName: 'Sunrow Farm', pricePerKg: 6.4, quantityAvailableKg: 10, note: null,
      }),
    );
  });
});
