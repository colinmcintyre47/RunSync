// frontend/src/components/AuthPage.tsx
// Sign-in and sign-up screen. This is the first thing a new user sees.
//
// One form handles both modes; the only difference is the display-name field and which
// endpoint is called. Both return the same AuthResponse containing a JWT.
//
// The copy mentions the Strava setup step up front rather than surprising people with it
// after they've registered — it is the one unusual thing RunSync asks of a new user, and
// burying it makes it feel like a problem instead of a one-minute step.
//
// → Calls: frontend/src/api/stravaApi.ts → login(), register(), storeAuth()
// → Rendered by: frontend/src/App.tsx when there is no stored session

import React, { useState } from 'react';
import { login, register, storeAuth } from '../api/stravaApi';
import type { AuthResponse } from '../types/strava';

interface AuthPageProps {
  onAuthenticated: (auth: AuthResponse) => void;
}

type Mode = 'login' | 'register';

export const AuthPage: React.FC<AuthPageProps> = ({ onAuthenticated }) => {
  const [mode, setMode] = useState<Mode>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      const auth =
        mode === 'login'
          ? await login(email.trim(), password)
          : await register(email.trim(), password, displayName.trim());

      storeAuth(auth);
      onAuthenticated(auth);
    } catch (err) {
      setError((err as Error).message);
      setIsSubmitting(false);
    }
  };

  const switchMode = () => {
    setMode(mode === 'login' ? 'register' : 'login');
    setError(null);
  };

  return (
    <div className="flex min-h-screen items-center justify-center px-4 py-12">
      <div className="w-full max-w-sm">
        <header className="mb-8 text-center">
          <h1 className="text-3xl font-bold tracking-tight text-gray-900">RunSync</h1>
          <p className="mt-2 text-sm text-gray-600">
            Your Strava runs, laid over your training plan.
          </p>
        </header>

        <form
          onSubmit={handleSubmit}
          className="space-y-4 rounded-xl border border-gray-200 bg-white p-6 shadow-sm"
        >
          <h2 className="text-lg font-semibold text-gray-900">
            {mode === 'login' ? 'Sign in' : 'Create your account'}
          </h2>

          {mode === 'register' && (
            <div>
              <label htmlFor="displayName" className="block text-sm font-medium text-gray-700">
                Name
              </label>
              <input
                id="displayName"
                type="text"
                autoComplete="name"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                required
                maxLength={64}
                className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-orange-500 focus:outline-none focus:ring-1 focus:ring-orange-500"
              />
            </div>
          )}

          <div>
            <label htmlFor="email" className="block text-sm font-medium text-gray-700">
              Email
            </label>
            <input
              id="email"
              type="email"
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-orange-500 focus:outline-none focus:ring-1 focus:ring-orange-500"
            />
          </div>

          <div>
            <label htmlFor="password" className="block text-sm font-medium text-gray-700">
              Password
            </label>
            <input
              id="password"
              type="password"
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              minLength={8}
              className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-orange-500 focus:outline-none focus:ring-1 focus:ring-orange-500"
            />
            {mode === 'register' && (
              <p className="mt-1 text-xs text-gray-500">At least 8 characters.</p>
            )}
          </div>

          {error && (
            <p role="alert" className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
              {error}
            </p>
          )}

          <button
            type="submit"
            disabled={isSubmitting}
            className="w-full rounded-lg bg-orange-500 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-orange-600 disabled:opacity-60"
          >
            {isSubmitting
              ? 'Please wait…'
              : mode === 'login'
                ? 'Sign in'
                : 'Create account'}
          </button>

          <p className="text-center text-sm text-gray-600">
            {mode === 'login' ? "Don't have an account?" : 'Already have an account?'}{' '}
            <button
              type="button"
              onClick={switchMode}
              className="font-medium text-orange-600 hover:text-orange-700"
            >
              {mode === 'login' ? 'Sign up' : 'Sign in'}
            </button>
          </p>
        </form>

        {mode === 'register' && (
          <p className="mt-4 rounded-lg bg-gray-100 px-4 py-3 text-xs leading-relaxed text-gray-600">
            <span className="font-medium text-gray-800">Heads up:</span> after signing up you'll
            create a free Strava API application and paste two values into RunSync. Strava limits
            each application to one athlete, so everyone brings their own — it keeps RunSync free.
            It takes about a minute and we walk you through it.
          </p>
        )}
      </div>
    </div>
  );
};
