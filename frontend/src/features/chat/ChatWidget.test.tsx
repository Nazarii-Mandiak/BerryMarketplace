import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ChatWidget } from './ChatWidget';
import type { ChatStreamEvent } from './sse';

const streamChatMessage = vi.fn(async (_id: string, _content: string, onEvent: (e: ChatStreamEvent) => void) => {
  onEvent({ type: 'tool_call', name: 'search_listings' });
  onEvent({ type: 'text', text: 'Two strawberry listings look great today.' });
  onEvent({ type: 'done' });
});

vi.mock('../../api/chat', () => ({
  getConversations: vi.fn().mockResolvedValue([]),
  createConversation: vi.fn().mockResolvedValue({ id: 'conv-1', title: 'New conversation', createdAt: '' }),
  getMessages: vi.fn().mockResolvedValue([]),
  streamChatMessage: (...args: Parameters<typeof streamChatMessage>) => streamChatMessage(...args),
}));
vi.mock('../../api/ai', () => ({
  getAiStatus: vi.fn().mockResolvedValue({ enabled: true }),
}));

describe('ChatWidget', () => {
  it('sends a message and renders streamed assistant text', async () => {
    render(<ChatWidget isAuthenticated />);
    await userEvent.click(await screen.findByRole('button', { name: /chat with berry/i }));
    await userEvent.type(screen.getByPlaceholderText(/ask about berries/i), 'anything sweet?');
    await userEvent.click(screen.getByRole('button', { name: /send/i }));
    expect(await screen.findByText('Two strawberry listings look great today.')).toBeInTheDocument();
    expect(screen.getByText('anything sweet?')).toBeInTheDocument();
  });

  it('renders the backend error frame instead of silently dropping it', async () => {
    streamChatMessage.mockImplementationOnce(async (_id, _content, onEvent) => {
      onEvent({ type: 'error', message: 'Something went wrong. Please try again.' });
    });
    render(<ChatWidget isAuthenticated />);
    await userEvent.click(await screen.findByRole('button', { name: /chat with berry/i }));
    await userEvent.type(screen.getByPlaceholderText(/ask about berries/i), 'anything sweet?');
    await userEvent.click(screen.getByRole('button', { name: /send/i }));
    expect(await screen.findByText('Something went wrong. Please try again.')).toBeInTheDocument();
  });
});
