import { type FormEvent, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { GoogleLogin, type CredentialResponse } from '@react-oauth/google';
import { login, loginWithGoogle } from '@/api/accounts';
import { ApiError } from '@/api/client';
import type { UserResponse } from '@/api/types';
import { CURRENT_USER_QUERY_KEY } from '@/features/auth/useCurrentUser';
import { BerryIcon } from '@/components/BerryIcon';

function validateEmail(email: string) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
}

const SignIn1 = () => {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const googleClientId = import.meta.env.VITE_GOOGLE_CLIENT_ID as string | undefined;

  function handleSignedIn(user: UserResponse) {
    queryClient.setQueryData(CURRENT_USER_QUERY_KEY, user);
    const from = (location.state as { from?: { pathname: string } })?.from?.pathname ?? '/market';
    navigate(from, { replace: true });
  }

  function handleAuthError(err: unknown, invalidCredentialsMessage: string) {
    if (err instanceof ApiError && err.status === 401) {
      setError(invalidCredentialsMessage);
    } else if (err instanceof ApiError) {
      setError(err.errors[0] ?? 'Something went wrong — try again.');
    } else {
      setError('Something went wrong — try again.');
    }
  }

  const mutation = useMutation({
    mutationFn: () => login({ email, password }),
    onSuccess: handleSignedIn,
    onError: (err) => handleAuthError(err, 'Invalid email or password.'),
  });

  const googleMutation = useMutation({
    mutationFn: (credential: string) => loginWithGoogle({ credential }),
    onSuccess: handleSignedIn,
    onError: (err) => handleAuthError(err, 'Google sign-in failed — try again.'),
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!email || !password) {
      setError('Please enter both email and password.');
      return;
    }
    if (!validateEmail(email)) {
      setError('Please enter a valid email address.');
      return;
    }
    setError('');
    mutation.mutate();
  }

  function handleGoogleSuccess(credentialResponse: CredentialResponse) {
    if (!credentialResponse.credential) {
      setError('Google sign-in failed — try again.');
      return;
    }
    setError('');
    googleMutation.mutate(credentialResponse.credential);
  }

  return (
    <div className="flex flex-col items-center justify-center w-full">
      <div className="relative z-10 w-full max-w-sm rounded-3xl bg-gradient-to-br from-[var(--panel)] to-[var(--ground-2)] backdrop-blur-sm border border-[var(--line)] shadow-[var(--shadow)] p-8 flex flex-col items-center">
        <div className="flex items-center justify-center w-14 h-14 rounded-full bg-[var(--ground-2)] mb-4 shadow-[var(--shadow)] [&>svg]:w-8 [&>svg]:h-8">
          <BerryIcon berryType="raspberries" />
        </div>
        <span className="text-xs font-bold uppercase tracking-[0.08em] text-[var(--ink-muted)] mb-1 text-center">
          Berrow
        </span>
        <h2 className="text-2xl font-extrabold text-[var(--ink)] mb-6 text-center">Log in</h2>

        <form onSubmit={handleSubmit} noValidate className="flex flex-col w-full gap-4">
          <div className="w-full flex flex-col gap-3">
            <div className="flex flex-col gap-1.5">
              <label htmlFor="signin-email" className="sr-only">
                Email
              </label>
              <input
                id="signin-email"
                placeholder="Email"
                type="email"
                autoComplete="email"
                value={email}
                className="w-full px-5 py-3 rounded-xl border-2 border-[var(--line-strong)] bg-[var(--ground)] text-[var(--ink)]! placeholder-[var(--ink-muted)] text-sm focus:outline-none focus:border-[var(--accent)]"
                onChange={(e) => setEmail(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <label htmlFor="signin-password" className="sr-only">
                Password
              </label>
              <input
                id="signin-password"
                placeholder="Password"
                type="password"
                autoComplete="current-password"
                value={password}
                className="w-full px-5 py-3 rounded-xl border-2 border-[var(--line-strong)] bg-[var(--ground)] text-[var(--ink)]! placeholder-[var(--ink-muted)] text-sm focus:outline-none focus:border-[var(--accent)]"
                onChange={(e) => setPassword(e.target.value)}
              />
            </div>
            {error && <div className="text-sm text-[var(--accent)] text-left">{error}</div>}
          </div>
          <hr className="border-[var(--line)]" />
          <button
            type="submit"
            disabled={mutation.isPending}
            className="w-full bg-[var(--ink)] text-[var(--ground)]! font-bold px-5 py-3 rounded-full shadow hover:bg-[var(--accent)] hover:text-[var(--accent-ink)]! transition text-sm disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {mutation.isPending ? 'Logging in…' : 'Log in'}
          </button>
        </form>

        {googleClientId && (
          <>
            <div className="flex items-center gap-3 w-full my-5">
              <hr className="flex-1 border-[var(--line)]" />
              <span className="text-xs text-[var(--ink-muted)]">or</span>
              <hr className="flex-1 border-[var(--line)]" />
            </div>
            <GoogleLogin
              onSuccess={handleGoogleSuccess}
              onError={() => setError('Google sign-in failed — try again.')}
              theme="outline"
              shape="pill"
              size="large"
              text="continue_with"
              width="320"
            />
          </>
        )}

        <div className="w-full text-center mt-5">
          <span className="text-xs text-[var(--ink-muted)]">
            Need an account?{' '}
            <Link to="/register" className="underline text-[var(--ink)] hover:text-[var(--accent)] font-semibold">
              Register
            </Link>
          </span>
        </div>
      </div>
    </div>
  );
};

export { SignIn1 };
