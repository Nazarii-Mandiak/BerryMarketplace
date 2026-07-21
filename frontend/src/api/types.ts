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
