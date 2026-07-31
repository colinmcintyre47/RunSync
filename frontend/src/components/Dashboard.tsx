// frontend/src/components/Dashboard.tsx
// The main signed-in view: header with sync controls, summary stats, and two tabs —
// the full run history and the fixed training plan.
//
// "All runs" is the default tab. It works for every user the moment they sync, whereas the
// training plan grid depends on a configured goal and a fixed 12-week window, so it can look
// empty for someone who just signed up.
//
// Onboarding order matters here. A brand new user has no Strava application, so the connect
// button renders as "Set up Strava access" and a banner explains why. Once credentials exist
// the banner disappears and the normal connect → sync flow takes over.
//
// Syncing refreshes both data sources, since a sync changes the plan view and the run log.
//
// → Data: useStravaActivities.ts (plan) + useRunData.ts (runs, stats, profile)
// → Children: StravaConnectButton, SyncStatus, StatsTiles, WeeklyLog, TrainingDay, CopyAllDataButton
// → Rendered by: frontend/src/App.tsx

import React, { useCallback, useState } from 'react';
import { CopyAllDataButton } from './CopyAllDataButton';
import { StatsTiles } from './StatsTiles';
import { StravaConnectButton } from './StravaConnectButton';
import { SyncStatusBar } from './SyncStatus';
import { TrainingDay } from './TrainingDay';
import { WeeklyLog } from './WeeklyLog';
import { useRunData } from '../hooks/useRunData';
import { useStravaActivities } from '../hooks/useStravaActivities';

interface DashboardProps {
  displayName: string;
  hasCredentials: boolean;
  onOpenSettings: () => void;
  onSignOut: () => void;
}

type Tab = 'runs' | 'plan';

export const Dashboard: React.FC<DashboardProps> = ({
  displayName,
  hasCredentials,
  onOpenSettings,
  onSignOut,
}) => {
  const [tab, setTab] = useState<Tab>('runs');

  const plan = useStravaActivities();
  const runData = useRunData();

  // A sync invalidates both views, so refresh them together.
  const handleSync = useCallback(async () => {
    await plan.triggerSync();
    await runData.refresh();
  }, [plan, runData]);

  const handleDisconnect = useCallback(async () => {
    await plan.refresh();
    await runData.refresh();
  }, [plan, runData]);

  const greetingName = runData.profile?.firstName || displayName;
  const error = runData.error ?? plan.error;

  return (
    <div className="mx-auto max-w-5xl px-4 py-8">
      <header className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div className="flex items-center gap-3">
          {runData.profile?.profileUrl && (
            <img
              src={runData.profile.profileUrl}
              alt=""
              className="h-11 w-11 rounded-full object-cover ring-1 ring-gray-200"
            />
          )}
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-gray-900">
              {greetingName ? `${greetingName}'s runs` : 'Your runs'}
            </h1>
            <div className="mt-1">
              <SyncStatusBar syncStatus={plan.syncStatus} isSyncing={plan.isSyncing} />
            </div>
          </div>
        </div>

        <div className="flex items-center gap-3">
          <StravaConnectButton
            syncStatus={plan.syncStatus}
            isSyncing={plan.isSyncing}
            hasCredentials={hasCredentials}
            onSyncClick={handleSync}
            onDisconnect={handleDisconnect}
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

      <div className="mb-6">
        <StatsTiles stats={runData.stats} isLoading={runData.isLoading} />
      </div>

      <div className="mb-4 flex flex-wrap items-center justify-between gap-3 border-b border-gray-200">
        <nav className="flex gap-1" aria-label="Views">
          <TabButton isActive={tab === 'runs'} onClick={() => setTab('runs')}>
            All runs
          </TabButton>
          <TabButton isActive={tab === 'plan'} onClick={() => setTab('plan')}>
            Training plan
          </TabButton>
        </nav>

        <div className="pb-2">
          <CopyAllDataButton
            data={{ profile: runData.profile, stats: runData.stats, weeks: runData.weeks }}
            disabled={runData.isLoading}
          />
        </div>
      </div>

      {tab === 'runs' ? (
        <WeeklyLog weeks={runData.weeks} isLoading={runData.isLoading} />
      ) : plan.isLoading ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="h-32 animate-pulse rounded-xl bg-gray-100" />
          ))}
        </div>
      ) : plan.days.length === 0 ? (
        <p className="rounded-xl border border-gray-200 bg-white p-8 text-center text-sm text-gray-500">
          No training plan days to show. Your plan is built from the goal configured for your
          account.
        </p>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {plan.days.map((day) => (
            <TrainingDay key={day.planDate} activity={day} />
          ))}
        </div>
      )}
    </div>
  );
};

const TabButton: React.FC<{
  isActive: boolean;
  onClick: () => void;
  children: React.ReactNode;
}> = ({ isActive, onClick, children }) => (
  <button
    onClick={onClick}
    aria-current={isActive ? 'page' : undefined}
    className={
      isActive
        ? 'border-b-2 border-orange-500 px-3 pb-2 pt-1 text-sm font-semibold text-gray-900'
        : 'border-b-2 border-transparent px-3 pb-2 pt-1 text-sm font-medium text-gray-500 transition hover:text-gray-700'
    }
  >
    {children}
  </button>
);
