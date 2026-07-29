import { apiRequest } from './client';
import type { GoogleLoginRequest, LoginRequest, RegisterRequest, UserResponse } from './types';

export function login(request: LoginRequest): Promise<UserResponse> {
  return apiRequest<UserResponse>('/accounts/login', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function loginWithGoogle(request: GoogleLoginRequest): Promise<UserResponse> {
  return apiRequest<UserResponse>('/accounts/google', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function register(request: RegisterRequest): Promise<UserResponse> {
  return apiRequest<UserResponse>('/accounts/register', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function logout(): Promise<void> {
  return apiRequest<void>('/accounts/logout', { method: 'POST' });
}

export function getMe(): Promise<UserResponse> {
  return apiRequest<UserResponse>('/accounts/me');
}
