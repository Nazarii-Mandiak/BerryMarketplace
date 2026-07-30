import { describe, expect, it, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ThemeProvider, useTheme } from './ThemeProvider';

function TestConsumer() {
  const { theme, setTheme, resolved } = useTheme();
  return (
    <div>
      <span data-testid="theme">{theme}</span>
      <span data-testid="resolved">{resolved}</span>
      <button onClick={() => setTheme('light')}>light</button>
      <button onClick={() => setTheme('dark')}>dark</button>
      <button onClick={() => setTheme('system')}>system</button>
    </div>
  );
}

function stubMatchMedia(matches: boolean) {
  let listeners: Array<(e: { matches: boolean }) => void> = [];
  const mql = {
    matches,
    media: '(prefers-color-scheme: dark)',
    addEventListener: (_: string, cb: (e: { matches: boolean }) => void) => {
      listeners.push(cb);
    },
    removeEventListener: (_: string, cb: (e: { matches: boolean }) => void) => {
      listeners = listeners.filter((l) => l !== cb);
    },
    dispatchEvent: vi.fn(),
  };
  window.matchMedia = vi.fn().mockReturnValue(mql);
  return {
    fireChange: (nextMatches: boolean) => {
      mql.matches = nextMatches;
      act(() => {
        listeners.forEach((cb) => cb({ matches: nextMatches }));
      });
    },
  };
}

describe('ThemeProvider', () => {
  it('selecting light sets the .dark class off and persists to localStorage', async () => {
    stubMatchMedia(true);
    const user = userEvent.setup();
    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'light' }));

    expect(screen.getByTestId('resolved')).toHaveTextContent('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);
    expect(localStorage.getItem('theme')).toBe('light');
  });

  it('selecting dark sets the .dark class on and persists to localStorage', async () => {
    stubMatchMedia(false);
    const user = userEvent.setup();
    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'dark' }));

    expect(screen.getByTestId('resolved')).toHaveTextContent('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
    expect(localStorage.getItem('theme')).toBe('dark');
  });

  it('selecting system removes the stored preference and follows the OS setting', async () => {
    const { fireChange } = stubMatchMedia(false);
    const user = userEvent.setup();
    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'dark' }));
    await user.click(screen.getByRole('button', { name: 'system' }));

    expect(localStorage.getItem('theme')).toBeNull();
    expect(screen.getByTestId('resolved')).toHaveTextContent('light');

    fireChange(true);

    expect(screen.getByTestId('resolved')).toHaveTextContent('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
  });

  it('does not react to OS theme changes while an explicit theme is selected', async () => {
    const { fireChange } = stubMatchMedia(false);
    const user = userEvent.setup();
    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'light' }));
    fireChange(true);

    expect(screen.getByTestId('resolved')).toHaveTextContent('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });
});
