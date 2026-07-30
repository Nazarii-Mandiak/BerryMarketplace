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
  pricePerKg: number;
  quantityAvailableKg: number;
  note: string | null;
  createdAt: string;
  aiTastingNotes: string | null;
  hasPhoto: boolean;
}

export type SearchMode = 'semantic' | 'keyword';

export interface SearchListingsResponse {
  mode: SearchMode;
  results: ListingResponse[];
}

export interface ReservationWithListingResponse {
  id: string;
  listingId: string;
  quantityKg: number;
  status: string;
  reservedAt: string;
  berryType: string;
  farmName: string;
  pricePerKg: number;
}

export interface CreateListingRequest {
  berryType: string;
  farmName: string;
  pricePerKg: number;
  quantityAvailableKg: number;
  note: string | null;
}

export interface UpdateListingRequest {
  berryType: string;
  farmName: string;
  pricePerKg: number;
  quantityAvailableKg: number;
  note: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface GoogleLoginRequest {
  credential: string;
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
  pricePerKg: number | null;
  quantityAvailableKg: number | null;
  note: string | null;
}

export interface ListingCopySuggestion {
  improvedDescription: string;
  suggestedPricePerKg: number;
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
