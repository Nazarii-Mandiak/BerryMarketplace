import { useEffect, useRef, useState } from 'react';
import { getAiStatus } from '../../api/ai';
import { createConversation, streamChatMessage } from '../../api/chat';

interface DisplayMessage {
  role: 'user' | 'assistant' | 'status';
  content: string;
}

export function ChatWidget({ isAuthenticated }: { isAuthenticated: boolean }) {
  const [enabled, setEnabled] = useState(false);
  const [open, setOpen] = useState(false);
  const [messages, setMessages] = useState<DisplayMessage[]>([]);
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const conversationIdRef = useRef<string | null>(null);

  useEffect(() => {
    if (!isAuthenticated) return;
    getAiStatus().then((s) => setEnabled(s.enabled)).catch(() => setEnabled(false));
  }, [isAuthenticated]);

  if (!isAuthenticated || !enabled) return null;

  async function send() {
    const content = input.trim();
    if (!content || busy) return;
    setInput('');
    setBusy(true);
    setMessages((m) => [...m, { role: 'user', content }]);
    try {
      conversationIdRef.current ??= (await createConversation()).id;
      await streamChatMessage(conversationIdRef.current, content, (event) => {
        if (event.type === 'tool_call') {
          setMessages((m) => [...m, { role: 'status', content: `Using ${event.name}…` }]);
        } else if (event.type === 'text' && event.text) {
          setMessages((m) => [...m, { role: 'assistant', content: event.text! }]);
        } else if (event.type === 'error') {
          setMessages((m) => [...m, { role: 'status', content: event.message ?? 'Something went wrong. Try again.' }]);
        }
      });
    } catch {
      setMessages((m) => [...m, { role: 'status', content: 'Something went wrong. Try again.' }]);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="chat-widget">
      {open && (
        <div className="chat-panel">
          <div className="chat-messages">
            {messages.map((message, i) => (
              <p key={i} className={`chat-message chat-message--${message.role}`}>
                {message.role === 'status' ? <em>{message.content}</em> : message.content}
              </p>
            ))}
          </div>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void send();
            }}
          >
            <input
              placeholder="Ask about berries…"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              disabled={busy}
            />
            <button type="submit" disabled={busy}>
              Send
            </button>
          </form>
        </div>
      )}
      <button type="button" className="chat-toggle" onClick={() => setOpen((o) => !o)}>
        Chat with Berry
      </button>
    </div>
  );
}
