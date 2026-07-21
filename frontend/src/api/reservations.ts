import { apiRequest } from './client';
import type { ReservationWithListingResponse } from './types';

export function getMyReservations(): Promise<ReservationWithListingResponse[]> {
  return apiRequest<ReservationWithListingResponse[]>('/reservations/mine');
}
