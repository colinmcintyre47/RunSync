// frontend/src/components/SyncStatus.tsx
// Small status bar showing the last sync time, activity count, and current sync state.
// The "X minutes ago" display auto-refreshes every 60 seconds via setInterval.
//
// → Receives syncStatus and isSyncing from useStravaActivities hook
// → Used by the main training plan page alongside StravaConnectButton.tsx

import React, { useEffect, useState } from 'react';
import { formatLastSynced } from '../utils/dateUtils';
import type { SyncStatus } from '../types/strava';

interface SyncStatusProps {
  syncStatus: SyncStatus | null;
  isSyncing: boolean;
}

export const SyncStatusBar: React.FC<SyncStatusProps> = ({ syncStatus, isSyncing }) => {
  // Local state to force re-render every minute so "X minutes ago" stays current
  const [, forceUpdate] = useState(0);

  useEffect(() => {
    const interval = setInterval(() => forceUpdate(n => n + 1), 60_000);
    return () => clearInterval(interval);
  }, []);

  if (isSyncing) {
    return (
      <div className="flex items-center gap-2 text-sm text-gray-500">
        <span className="inline-block h-3 w-3 animate-spin rounded-full border-2 border-orange-400 border-t-transparent" />
        <span>Syncing with Strava...</span>
      </div>
    );
  }

  if (!syncStatus) return null;

  const { lastSyncedAt, totalActivities, isConnected } = syncStatus;

  if (!isConnected) {
    return (
      <div className="text-sm text-gray-400">
        Strava not connected
      </div>
    );
  }

  return (
    <div className="flex items-center gap-1.5 text-sm text-gray-500">
      <span className="inline-block h-2 w-2 rounded-full bg-emerald-400" aria-hidden="true" />
      <span>
        Last synced: <strong>{formatLastSynced(lastSyncedAt)}</strong>
      </span>
      <span className="text-gray-300">·</span>
      <span>{totalActivities} activit{totalActivities !== 1 ? 'ies' : 'y'}</span>
    </div>
  );
};
