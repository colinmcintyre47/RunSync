// frontend/src/App.tsx
// Root component: owns the session, the current view, and the Strava setup state.
//
// There are only two views, so this uses plain state rather than a router. The OAuth round
// trip returns to "/" with a ?strava=... query param, which is read once on mount and then
// stripped from the URL so a refresh doesn't replay the banner.
//
// New-user routing: after registering, a user has no Strava application, so they land
// directly in Settings. That is the only screen that can make progress at that point —
// dropping them on an empty training plan would leave them guessing.
//
// → Children: AuthPage.tsx, Dashboard.tsx, SettingsPage.tsx
// → Session storage: frontend/src/api/stravaApi.ts → storeAuth/clearAuth/getStoredAuth

import React, { useCallback, useEffect, useState } from 'react';
import { AuthPage } from './components/AuthPage';
import { Dashboard } from './components/Dashboard';
import { SettingsPage } from './components/SettingsPage';
import {
  ApiError,
  clearAuth,
  getStoredAuth,
  getStravaCredentialStatus,
} from './api/stravaApi';
import type { AuthResponse, StravaCredentialStatus } from './types/strava';

type View = 'dashboard' | 'settings';

/** Messages for the ?strava=... values the backend callback redirects with. */
const OAUTH_MESSAGES: Record<string, { text: string; tone: 'success' | 'error' }> = {
  connected: { text: 'Strava connected. Hit "Sync Now" to pull in your runs.', tone: 'success' },
  denied: { text: 'Strava authorization was cancelled.', tone: 'error' },
  invalid_credentials: {
    text:
      'Strava rejected your API application. Double-check the Client ID, Client Secret, and ' +
      'callback domain in Settings.',
    tone: 'error',
  },
};

export const App: React.FC = () => {
  const [session, setSession] = useState(() => getStoredAuth());
  const [view, setView] = useState<View>('dashboard');
  const [credentials, setCredentials] = useState<StravaCredentialStatus | null>(null);
  const [isCheckingCredentials, setIsCheckingCredentials] = useState(false);
  const [banner, setBanner] = useState<{ text: string; tone: 'success' | 'error' } | null>(null);

  const signOut = useCallback(() => {
    clearAuth();
    setSession(null);
    setCredentials(null);
    setView('dashboard');
  }, []);

  // Read the OAuth result once, then strip it from the URL so refreshing doesn't repeat it.
  useEffect(() => {
    const result = new URLSearchParams(window.location.search).get('strava');
    if (!result) return;

    setBanner(OAUTH_MESSAGES[result] ?? null);
    window.history.replaceState({}, '', window.location.pathname);
  }, []);

  // Whether the user has registered their own Strava application decides where they land.
  useEffect(() => {
    if (!session) return;

    let cancelled = false;
    setIsCheckingCredentials(true);

    getStravaCredentialStatus()
      .then((status) => {
        if (cancelled) return;
        setCredentials(status);
        // Send first-time users straight to the one screen that can move them forward.
        if (!status.isConfigured) setView('settings');
      })
      .catch((err) => {
        if (cancelled) return;
        // An expired or invalid JWT means the stored session is useless — start over
        // rather than leaving the user staring at an unexplained error.
        if (err instanceof ApiError && err.status === 401) {
          signOut();
        }
      })
      .finally(() => {
        if (!cancelled) setIsCheckingCredentials(false);
      });

    return () => {
      cancelled = true;
    };
  }, [session, signOut]);

  const handleAuthenticated = (auth: AuthResponse) => {
    setSession({ token: auth.token, displayName: auth.displayName });
    setBanner(null);
  };

  if (!session) {
    return <AuthPage onAuthenticated={handleAuthenticated} />;
  }

  // Hold the first paint until we know whether setup is needed, otherwise a returning user
  // sees the "set up Strava" prompt flash before the real state arrives.
  if (isCheckingCredentials && credentials === null) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <span className="h-6 w-6 animate-spin rounded-full border-2 border-orange-400 border-t-transparent" />
      </div>
    );
  }

  return (
    <>
      {banner && (
        <div
          role="status"
          className={
            banner.tone === 'success'
              ? 'bg-green-50 px-4 py-3 text-center text-sm text-green-800'
              : 'bg-red-50 px-4 py-3 text-center text-sm text-red-800'
          }
        >
          {banner.text}
          <button
            onClick={() => setBanner(null)}
            className="ml-3 font-medium underline opacity-70 hover:opacity-100"
          >
            Dismiss
          </button>
        </div>
      )}

      {view === 'settings' ? (
        <SettingsPage
          onBack={() => setView('dashboard')}
          onCredentialsChange={setCredentials}
        />
      ) : (
        <Dashboard
          displayName={session.displayName}
          hasCredentials={credentials?.isConfigured ?? false}
          onOpenSettings={() => setView('settings')}
          onSignOut={signOut}
        />
      )}
    </>
  );
};
