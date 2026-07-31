// frontend/src/components/StravaConnectButton.tsx
// Handles the Strava OAuth connection lifecycle in the UI.
//
// States:
//   No API application → prompt to finish setup (OAuth cannot start without credentials)
//   Not connected      → "Connect Strava" button → redirects to Strava OAuth
//   Connected          → "Sync Now" button + "Disconnect" link
//   Syncing            → loading spinner inside the "Sync Now" button
//
// On connect: calls getStravaAuthUrl() then does a full-page redirect to Strava.
// After Strava redirects back, the backend callback endpoint handles the code exchange
// and redirects here with ?strava=connected. The parent component should detect
// this query param and call refresh() to reload the training plan.
//
// The hasCredentials gate matters: every user authorizes against their OWN Strava application
// (see StravaCredentialsForm.tsx), so /api/strava/authorize returns a 400 until one is saved.
// Gating here turns that into a clear prompt instead of a failed click.
//
// → Calls: frontend/src/api/stravaApi.ts → getStravaAuthUrl(), disconnectStrava()
// → sync and syncStatus come from useStravaActivities hook in the parent

import React, { useState } from 'react';
import { disconnectStrava, getStravaAuthUrl } from '../api/stravaApi';
import type { SyncStatus } from '../types/strava';

interface StravaConnectButtonProps {
  syncStatus: SyncStatus | null;
  isSyncing: boolean;
  // False until the user has saved their own Strava API application credentials.
  hasCredentials: boolean;
  onSyncClick: () => Promise<void>;
  onDisconnect: () => void;
  // Invoked when the user clicks through from the "finish setup" prompt.
  onSetupClick?: () => void;
}

export const StravaConnectButton: React.FC<StravaConnectButtonProps> = ({
  syncStatus,
  isSyncing,
  hasCredentials,
  onSyncClick,
  onDisconnect,
  onSetupClick,
}) => {
  const [isConnecting, setIsConnecting] = useState(false);
  const [isDisconnecting, setIsDisconnecting] = useState(false);
  const [connectError, setConnectError] = useState<string | null>(null);

  const handleConnect = async () => {
    setIsConnecting(true);
    setConnectError(null);
    try {
      const authUrl = await getStravaAuthUrl();
      // Full-page redirect — Strava will redirect back to the backend callback endpoint
      window.location.href = authUrl;
    } catch (err) {
      // Surface the reason rather than silently resetting — the most likely cause is a
      // credential problem the user can actually fix.
      setConnectError((err as Error).message);
      setIsConnecting(false);
    }
  };

  const handleDisconnect = async () => {
    if (!confirm('Disconnect Strava? Your cached activities will be kept.')) return;
    setIsDisconnecting(true);
    try {
      await disconnectStrava();
      onDisconnect();
    } finally {
      setIsDisconnecting(false);
    }
  };

  const isConnected = syncStatus?.isConnected ?? false;

  // OAuth cannot start until the user has registered their own Strava application.
  if (!hasCredentials) {
    return (
      <button
        onClick={onSetupClick}
        className="flex items-center gap-2 rounded-lg border border-orange-300 bg-orange-50 px-4 py-2 text-sm font-semibold text-orange-700 transition hover:bg-orange-100"
      >
        <StravaWordmark />
        Set up Strava access
      </button>
    );
  }

  if (!isConnected) {
    return (
      <div className="flex flex-col items-start gap-1">
        <button
          onClick={handleConnect}
          disabled={isConnecting}
          className="flex items-center gap-2 rounded-lg bg-orange-500 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-orange-600 disabled:opacity-60"
        >
          {isConnecting ? (
            <>
              <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
              Connecting…
            </>
          ) : (
            <>
              <StravaWordmark />
              Connect Strava
            </>
          )}
        </button>

        {connectError && (
          <p role="alert" className="max-w-xs text-xs text-red-600">
            {connectError}
          </p>
        )}
      </div>
    );
  }

  return (
    <div className="flex items-center gap-3">
      <button
        onClick={onSyncClick}
        disabled={isSyncing}
        className="flex items-center gap-2 rounded-lg bg-orange-500 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-orange-600 disabled:opacity-60"
      >
        {isSyncing ? (
          <>
            <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
            Syncing…
          </>
        ) : (
          'Sync Now'
        )}
      </button>

      <button
        onClick={handleDisconnect}
        disabled={isDisconnecting}
        className="text-sm text-gray-400 hover:text-gray-600 disabled:opacity-40"
      >
        Disconnect
      </button>
    </div>
  );
};

function StravaWordmark() {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="currentColor"
      aria-label="Strava"
    >
      <path d="M15.387 17.944l-2.089-4.116h-3.065L15.387 24l5.15-10.172h-3.066m-7.008-5.599l2.836 5.598h4.172L10.463 0l-7 13.828h4.169" />
    </svg>
  );
}
