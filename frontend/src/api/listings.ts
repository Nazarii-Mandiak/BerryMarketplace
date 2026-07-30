import { apiRequest } from './client';
import type { CreateListingRequest, ListingResponse, SearchListingsResponse, UpdateListingRequest } from './types';

export function getListings(): Promise<ListingResponse[]> {
  return apiRequest<ListingResponse[]>('/listings');
}

export function getListing(id: string): Promise<ListingResponse> {
  return apiRequest<ListingResponse>(`/listings/${id}`);
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

export function updateListing(id: string, request: UpdateListingRequest): Promise<ListingResponse> {
  return apiRequest<ListingResponse>(`/listings/${id}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export function deleteListing(id: string): Promise<void> {
  return apiRequest<void>(`/listings/${id}`, { method: 'DELETE' });
}

export function reserveListing(listingId: string, quantityKg: number): Promise<void> {
  return apiRequest<void>(`/listings/${listingId}/reservations`, {
    method: 'POST',
    body: JSON.stringify({ quantityKg }),
  });
}

export function uploadListingPhoto(id: string, file: File): Promise<void> {
  const formData = new FormData();
  formData.append('photo', file);
  return apiRequest<void>(`/listings/${id}/photo`, { method: 'POST', body: formData });
}

export function deleteListingPhoto(id: string): Promise<void> {
  return apiRequest<void>(`/listings/${id}/photo`, { method: 'DELETE' });
}
