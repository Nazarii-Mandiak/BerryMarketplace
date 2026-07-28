import { ApiError, apiRequest } from './client';
import type { ChatConversation, ChatMessage } from './types';
import { extractSseEvents, type ChatStreamEvent } from '../features/chat/sse';

export function getConversations(): Promise<ChatConversation[]> {
  return apiRequest<ChatConversation[]>('/chat/conversations');
}

export function createConversation(title?: string): Promise<ChatConversation> {
  return apiRequest<ChatConversation>('/chat/conversations', {
    method: 'POST',
    body: JSON.stringify({ title: title ?? null }),
  });
}

export function getMessages(conversationId: string): Promise<ChatMessage[]> {
  return apiRequest<ChatMessage[]>(`/chat/conversations/${conversationId}/messages`);
}

export async function streamChatMessage(
  conversationId: string,
  content: string,
  onEvent: (event: ChatStreamEvent) => void,
): Promise<void> {
  const response = await fetch(`/api/chat/conversations/${conversationId}/messages`, {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ content }),
  });
  if (!response.ok || !response.body) {
    throw new ApiError(response.status, ['Chat request failed']);
  }
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const { events, rest } = extractSseEvents(buffer);
    buffer = rest;
    events.forEach(onEvent);
  }
}
