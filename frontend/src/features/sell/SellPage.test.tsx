import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../testUtils';
import { SellPage } from './SellPage';
import * as listingsApi from '../../api/listings';
import * as aiApi from '../../api/ai';
import { ApiError } from '../../api/client';

vi.mock('../../api/listings');
vi.mock('../../api/ai');

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
    await user.type(screen.getByLabelText('Price per pint ($)'), '6.40');
    await user.type(screen.getByLabelText('Pints available'), '10');

    const form = screen.getByRole('button', { name: 'Post listing' }).closest('form')!;
    fireEvent.submit(form);

    expect(await screen.findByText('BerryType is required.')).toBeInTheDocument();
  });

  it('submits a new listing with the entered fields', async () => {
    vi.mocked(listingsApi.createListing).mockResolvedValue({
      id: 'listing-1', sellerId: 'user-1', berryType: 'Tayberries', farmName: 'Sunrow Farm',
      pricePerPint: 6.4, quantityAvailable: 10, note: null, createdAt: new Date().toISOString(),
      aiTastingNotes: null,
    });
    const user = userEvent.setup();

    renderWithProviders(<SellPage />, { route: '/sell' });

    await user.type(screen.getByLabelText('Berry'), 'Tayberries');
    await user.type(screen.getByLabelText('Farm or garden'), 'Sunrow Farm');
    await user.type(screen.getByLabelText('Price per pint ($)'), '6.40');
    await user.type(screen.getByLabelText('Pints available'), '10');
    await user.click(screen.getByRole('button', { name: 'Post listing' }));

    await waitFor(() =>
      expect(listingsApi.createListing).toHaveBeenCalledWith({
        berryType: 'Tayberries', farmName: 'Sunrow Farm', pricePerPint: 6.4, quantityAvailable: 10, note: null,
      }),
    );
  });

  it('fills the form from the AI suggestion', async () => {
    vi.mocked(aiApi.getAiStatus).mockResolvedValue({ enabled: true });
    vi.mocked(aiApi.suggestListing).mockResolvedValue({
      improvedDescription: 'Sun-ripened and jam-ready',
      suggestedPricePerPint: 6.25,
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
});
