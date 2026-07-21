import { apiRequest } from './client';
import type { CreateListingRequest, ListingResponse } from './types';

export function getListings(): Promise<ListingResponse[]> {
  return apiRequest<ListingResponse[]>('/listings');
}

export function createListing(request: CreateListingRequest): Promise<ListingResponse> {
  return apiRequest<ListingResponse>('/listings', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function reserveListing(listingId: string): Promise<void> {
  return apiRequest<void>(`/listings/${listingId}/reservations`, { method: 'POST' });
}
