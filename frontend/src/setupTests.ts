import '@testing-library/jest-dom/vitest';
import { afterEach, beforeEach, vi } from 'vitest';
import { cleanup } from '@testing-library/react';

// Vite merges VITE_-prefixed process env vars into import.meta.env in test mode. If a
// developer has VITE_GOOGLE_CLIENT_ID set in their local frontend/.env (e.g. to test the
// Google sign-in feature), that value leaks into every test file, causing SignIn1 to render
// the real <GoogleLogin> outside of a GoogleOAuthProvider — which only main.tsx supplies, not
// renderWithProviders (see testUtils.tsx). Force it to empty by default; setupFiles hooks run
// before per-file hooks, so a test file's own beforeEach (e.g.
// modern-stunning-sign-in.test.tsx stubbing 'test-client-id') still wins where it needs to.
beforeEach(() => {
  vi.stubEnv('VITE_GOOGLE_CLIENT_ID', '');
});

afterEach(() => {
  vi.unstubAllEnvs();
  cleanup();
});
