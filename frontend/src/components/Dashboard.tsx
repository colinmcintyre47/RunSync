// frontend/src/components/Dashboard.tsx
// The main training plan view: header with sync controls, then the plan day cards.
//
// Onboarding order matters here. A brand new user has no Strava application, so the connect
// button renders as "Set up Strava access" and a banner explains why. Once credentials exist
// the banner disappears and the normal connect → sync flow takes over.
//
// → Data: frontend/src/hooks/useStravaActivities.ts
// → Children: StravaConnectButton.tsx, SyncStatus.tsx, TrainingDay.tsx
// → Rendered by: frontend/src/App.tsx

import React from 'react';
import { StravaConnectButton } from './StravaConnectButton';
import { SyncStatusBar } from './SyncStatus';
import { TrainingDay } from './TrainingDay';
import { useStravaActivities } from '../hooks/useStravaActivities';

interface DashboardProps {
  displayName: string;
  hasCredentials: boolean;
  onOpenSettings: () => void;
  onSignOut: () => void;
}

export const Dashboard: React.FC<DashboardProps> = ({
  displayName,
  hasCredentials,
  onOpenSettings,
  onSignOut,
}) => {
  const { days, syncStatus, isLoading, isSyncing, error, triggerSync, refresh } =
    useStravaActivities();

  return (
    <div className="mx-auto max-w-6xl px-4 py-8">
      <header className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-gray-900">
            {displayName ? `${displayName}'s training plan` : 'Training plan'}
          </h1>
          <div className="mt-1">
            <SyncStatusBar syncStatus={syncStatus} isSyncing={isSyncing} />
          </div>
        </div>

        <div className="flex items-center gap-3">
          <StravaConnectButton
            syncStatus={syncStatus}
            isSyncing={isSyncing}
            hasCredentials={hasCredentials}
            onSyncClick={triggerSync}
            onDisconnect={refresh}
            onSetupClick={onOpenSettings}
          />
          <button
            onClick={onOpenSettings}
            className="text-sm text-gray-500 transition hover:text-gray-700"
          >
            Settings
          </button>
          <button
            onClick={onSignOut}
            className="text-sm text-gray-400 transition hover:text-gray-600"
          >
            Sign out
          </button>
        </div>
      </header>

      {!hasCredentials && (
        <div className="mb-6 rounded-xl border border-orange-200 bg-orange-50 p-4">
          <h2 className="text-sm font-semibold text-orange-900">One step before you can sync</h2>
          <p className="mt-1 text-sm text-orange-800">
            Strava allows one athlete per API application, so RunSync asks you to create your own
            free application. It takes about a minute.
          </p>
          <button
            onClick={onOpenSettings}
            className="mt-3 rounded-lg bg-orange-500 px-3 py-1.5 text-sm font-semibold text-white transition hover:bg-orange-600"
          >
            Show me how
          </button>
        </div>
      )}

      {error && (
        <p role="alert" className="mb-6 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </p>
      )}

      {isLoading ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="h-32 animate-pulse rounded-xl bg-gray-100" />
          ))}
        </div>
      ) : days.length === 0 ? (
        <p className="rounded-xl border border-gray-200 bg-white p-8 text-center text-sm text-gray-500">
          No training days to show yet.
        </p>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {days.map((day) => (
            <TrainingDay key={day.planDate} activity={day} />
          ))}
        </div>
      )}
    </div>
  );
};
