import '@testing-library/jest-dom/vitest';
import { afterEach, beforeEach, vi } from 'vitest';
import { cleanup } from '@testing-library/react';

// Vitest's jsdom environment doesn't expose window.localStorage in this project's setup
// (jsdom's own getter works fine standalone, but the accessor doesn't survive Vitest's
// global copy), so ThemeProvider's calls to localStorage would throw here. Polyfill a
// minimal in-memory Storage only when the real one is missing.
if (typeof window.localStorage === 'undefined') {
  const store = new Map<string, string>();
  Object.defineProperty(window, 'localStorage', {
    value: {
      getItem: (key: string) => (store.has(key) ? store.get(key)! : null),
      setItem: (key: string, value: string) => {
        store.set(key, String(value));
      },
      removeItem: (key: string) => {
        store.delete(key);
      },
      clear: () => {
        store.clear();
      },
      key: (index: number) => Array.from(store.keys())[index] ?? null,
      get length() {
        return store.size;
      },
    },
    writable: true,
  });
}

// Vite merges VITE_-prefixed process env vars into import.meta.env in test mode. If a
// developer has VITE_GOOGLE_CLIENT_ID set in their local frontend/.env (e.g. to test the
// Google sign-in feature), that value leaks into every test file, causing SignIn1 to render
// the real <GoogleLogin> outside of a GoogleOAuthProvider — which only main.tsx supplies, not
// renderWithProviders (see testUtils.tsx). Force it to empty by default; setupFiles hooks run
// before per-file hooks, so a test file's own beforeEach (e.g.
// modern-stunning-sign-in.test.tsx stubbing 'test-client-id') still wins where it needs to.
beforeEach(() => {
  vi.stubEnv('VITE_GOOGLE_CLIENT_ID', '');

  // jsdom doesn't implement matchMedia at all. ThemeProvider (mounted by every
  // renderWithProviders call) queries it on init, so every test needs a default stub;
  // ThemeProvider.test.tsx overrides this per-case to simulate system dark/light.
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }));
});

afterEach(() => {
  vi.unstubAllEnvs();
  cleanup();
  localStorage.clear();
  document.documentElement.classList.remove('dark');
});
