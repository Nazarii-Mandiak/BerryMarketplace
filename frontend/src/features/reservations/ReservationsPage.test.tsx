import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../testUtils';
import { ReservationsPage } from './ReservationsPage';
import * as reservationsApi from '../../api/reservations';

vi.mock('../../api/reservations');

describe('ReservationsPage', () => {
  it('shows an empty state with a link to the market when there are no reservations', async () => {
    vi.mocked(reservationsApi.getMyReservations).mockResolvedValue([]);

    renderWithProviders(<ReservationsPage />, { route: '/reservations' });

    expect(await screen.findByText(/No reservations yet/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'this way' })).toHaveAttribute('href', '/market');
  });

  it('lists reservations with their listing details', async () => {
    vi.mocked(reservationsApi.getMyReservations).mockResolvedValue([
      {
        id: 'r1', listingId: 'l1', quantity: 1, status: 'Pending', reservedAt: '2026-07-20T12:00:00Z',
        berryType: 'Gooseberries', farmName: 'Old Stone Orchard', pricePerPint: 8.5,
      },
    ]);

    renderWithProviders(<ReservationsPage />, { route: '/reservations' });

    expect(await screen.findByText('Gooseberries')).toBeInTheDocument();
    expect(screen.getByText('Old Stone Orchard')).toBeInTheDocument();
    expect(screen.getByText('Pending')).toBeInTheDocument();
  });
});
