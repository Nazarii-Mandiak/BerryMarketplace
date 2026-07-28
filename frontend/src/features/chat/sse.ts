export interface ChatStreamEvent {
  type: 'text' | 'tool_call' | 'done';
  text?: string;
  name?: string;
}

// Pull complete `data: {...}\n\n` frames off the front of the buffer;
// return whatever partial frame remains as `rest`.
export function extractSseEvents(buffer: string): { events: ChatStreamEvent[]; rest: string } {
  const events: ChatStreamEvent[] = [];
  let rest = buffer;
  let idx: number;
  while ((idx = rest.indexOf('\n\n')) >= 0) {
    const frame = rest.slice(0, idx);
    rest = rest.slice(idx + 2);
    const dataLine = frame.split('\n').find((line) => line.startsWith('data: '));
    if (dataLine) {
      events.push(JSON.parse(dataLine.slice(6)) as ChatStreamEvent);
    }
  }
  return { events, rest };
}
