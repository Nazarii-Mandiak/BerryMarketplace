export interface UserResponse {
  id: string;
  email: string;
  displayName: string;
}

export interface ListingResponse {
  id: string;
  sellerId: string;
  berryType: string;
  farmName: string;
  pricePerPint: number;
  quantityAvailable: number;
  note: string | null;
  createdAt: string;
  aiTastingNotes: string | null;
}

export type SearchMode = 'semantic' | 'keyword';

export interface SearchListingsResponse {
  mode: SearchMode;
  results: ListingResponse[];
}

export interface ReservationWithListingResponse {
  id: string;
  listingId: string;
  quantity: number;
  status: string;
  reservedAt: string;
  berryType: string;
  farmName: string;
  pricePerPint: number;
}

export interface CreateListingRequest {
  berryType: string;
  farmName: string;
  pricePerPint: number;
  quantityAvailable: number;
  note: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}

export interface AiStatus {
  enabled: boolean;
}

export interface ListingDraft {
  berryType: string;
  farmName: string;
  pricePerPint: number | null;
  quantityAvailable: number | null;
  note: string | null;
}

export interface ListingCopySuggestion {
  improvedDescription: string;
  suggestedPricePerPint: number;
  reasoning: string;
}

export interface ChatConversation {
  id: string;
  title: string;
  createdAt: string;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  createdAt: string;
}
