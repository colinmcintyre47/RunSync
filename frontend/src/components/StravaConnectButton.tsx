// frontend/src/components/StravaConnectButton.tsx
// Handles the Strava OAuth connection lifecycle in the UI.
//
// States:
//   Not connected → "Connect Strava" button → redirects to Strava OAuth
//   Connected     → "Sync Now" button + "Disconnect" link
//   Syncing       → loading spinner inside the "Sync Now" button
//
// On connect: calls getStravaAuthUrl() then does a full-page redirect to Strava.
// After Strava redirects back, the backend callback endpoint handles the code exchange
// and redirects here with ?strava=connected. The parent component should detect
// this query param and call refresh() to reload the training plan.
//
// → Calls: frontend/src/api/stravaApi.ts → getStravaAuthUrl(), disconnectStrava()
// → sync and syncStatus come from useStravaActivities hook in the parent

import React, { useState } from 'react';
import { disconnectStrava, getStravaAuthUrl } from '../api/stravaApi';
import type { SyncStatus } from '../types/strava';

interface StravaConnectButtonProps {
  syncStatus: SyncStatus | null;
  isSyncing: boolean;
  onSyncClick: () => Promise<void>;
  onDisconnect: () => void;
}

export const StravaConnectButton: React.FC<StravaConnectButtonProps> = ({
  syncStatus,
  isSyncing,
  onSyncClick,
  onDisconnect,
}) => {
  const [isConnecting, setIsConnecting] = useState(false);
  const [isDisconnecting, setIsDisconnecting] = useState(false);

  const handleConnect = async () => {
    setIsConnecting(true);
    try {
      const authUrl = await getStravaAuthUrl();
      // Full-page redirect — Strava will redirect back to the backend callback endpoint
      window.location.href = authUrl;
    } catch {
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

  if (!isConnected) {
    return (
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
