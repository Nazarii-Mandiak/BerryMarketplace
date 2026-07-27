import { apiRequest } from './client';
import type { AiStatus, ListingCopySuggestion, ListingDraft } from './types';

export function getAiStatus(): Promise<AiStatus> {
  return apiRequest<AiStatus>('/ai/status');
}

export function suggestListing(draft: ListingDraft): Promise<ListingCopySuggestion> {
  return apiRequest<ListingCopySuggestion>('/ai/listing-assist', {
    method: 'POST',
    body: JSON.stringify(draft),
  });
}
