import { apiRequest } from './client';
import type { CreateListingRequest, ListingResponse, SearchListingsResponse } from './types';

export function getListings(): Promise<ListingResponse[]> {
  return apiRequest<ListingResponse[]>('/listings');
}

export function searchListings(q: string): Promise<SearchListingsResponse> {
  return apiRequest<SearchListingsResponse>(`/listings/search?q=${encodeURIComponent(q)}`);
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
