import { describe, expect, it } from 'vitest';
import { extractSseEvents } from './sse';

describe('extractSseEvents', () => {
  it('parses complete frames and keeps the partial tail', () => {
    const { events, rest } = extractSseEvents(
      'data: {"type":"tool_call","name":"search_listings"}\n\ndata: {"type":"text","text":"Hi"}\n\ndata: {"ty',
    );
    expect(events).toEqual([
      { type: 'tool_call', name: 'search_listings' },
      { type: 'text', text: 'Hi' },
    ]);
    expect(rest).toBe('data: {"ty');
  });
});
