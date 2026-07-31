// frontend/src/components/WeeklyLog.tsx
// The complete history of synced runs, grouped into Mon–Sun weeks, newest first.
//
// This is the view that shows *all* of a user's data — unlike the training plan grid, it
// doesn't depend on a configured goal or a fixed 12-week window, so it works for anyone the
// moment they sync.
//
// Each run row links back to the activity on Strava, which is also a requirement of Strava's
// API terms for displaying activity data.
//
// → Data: WeekSummaryDto via useRunData.ts
// → Rendered by: Dashboard.tsx

import React from 'react';
import type { RunActivity, WeekSummary } from '../types/strava';

interface WeeklyLogProps {
  weeks: WeekSummary[];
  isLoading: boolean;
}

export const WeeklyLog: React.FC<WeeklyLogProps> = ({ weeks, isLoading }) => {
  if (isLoading) {
    return (
      <div className="space-y-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <div key={i} className="h-32 animate-pulse rounded-xl bg-gray-100" />
        ))}
      </div>
    );
  }

  if (weeks.length === 0) {
    return (
      <p className="rounded-xl border border-gray-200 bg-white p-8 text-center text-sm text-gray-500">
        No runs synced yet. Connect Strava and hit <strong>Sync Now</strong> to pull in your
        activity history.
      </p>
    );
  }

  return (
    <div className="space-y-4">
      {weeks.map((week) => (
        <section key={week.weekStart} className="rounded-xl border border-gray-200 bg-white">
          <header className="flex flex-wrap items-baseline justify-between gap-2 border-b border-gray-100 px-4 py-3">
            <h3 className="text-sm font-semibold text-gray-900">{week.weekLabel}</h3>
            <p className="text-sm text-gray-500">
              <strong className="text-gray-900">{week.totalMiles.toFixed(1)}</strong> mi ·{' '}
              {week.runCount} run{week.runCount === 1 ? '' : 's'}
            </p>
          </header>

          <ul className="divide-y divide-gray-100">
            {week.runs.map((run) => (
              <RunRow key={run.stravaId} run={run} />
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
};

const RunRow: React.FC<{ run: RunActivity }> = ({ run }) => (
  <li className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 px-4 py-3">
    <div className="min-w-0 flex-1">
      <div className="flex items-center gap-2">
        <a
          href={`https://www.strava.com/activities/${run.stravaId}`}
          target="_blank"
          rel="noopener noreferrer"
          className="truncate text-sm font-medium text-gray-900 hover:text-orange-600"
        >
          {run.name}
        </a>
        {run.prCount > 0 && (
          <span className="flex-none rounded-full bg-amber-100 px-1.5 py-0.5 text-[10px] font-semibold text-amber-700">
            {run.prCount} PR{run.prCount === 1 ? '' : 's'}
          </span>
        )}
      </div>
      <p className="mt-0.5 text-xs text-gray-400">
        {new Date(run.date).toLocaleDateString('en-US', {
          weekday: 'short',
          month: 'short',
          day: 'numeric',
        })}
        {run.sportType && run.sportType !== 'Run' && ` · ${run.sportType}`}
        {run.workoutTypeLabel && run.workoutTypeLabel !== 'Run' && ` · ${run.workoutTypeLabel}`}
      </p>
    </div>

    <dl className="flex items-center gap-4 text-sm tabular-nums">
      <Metric label="mi" value={run.miles.toFixed(1)} />
      <Metric label="pace" value={run.pace} />
      <Metric label="time" value={run.movingTime} />
      {run.avgHeartrate != null && (
        <Metric label="bpm" value={String(Math.round(run.avgHeartrate))} />
      )}
    </dl>
  </li>
);

const Metric: React.FC<{ label: string; value: string }> = ({ label, value }) => (
  <div className="text-right">
    <dd className="font-semibold text-gray-900">{value}</dd>
    <dt className="text-[10px] uppercase tracking-wide text-gray-400">{label}</dt>
  </div>
);
