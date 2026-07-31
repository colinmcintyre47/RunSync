// frontend/src/components/StatsTiles.tsx
// The four summary numbers across the top of the dashboard.
//
// → Data: DashboardStatsDto via useRunData.ts
// → Rendered by: Dashboard.tsx

import React from 'react';
import type { DashboardStats } from '../types/strava';

interface StatsTilesProps {
  stats: DashboardStats | null;
  isLoading: boolean;
}

export const StatsTiles: React.FC<StatsTilesProps> = ({ stats, isLoading }) => {
  if (isLoading) {
    return (
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="h-20 animate-pulse rounded-xl bg-gray-100" />
        ))}
      </div>
    );
  }

  if (!stats) return null;

  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      <Tile label="Miles this week" value={stats.milesThisWeek.toFixed(1)} />
      <Tile label="Runs this week" value={String(stats.runsThisWeek)} />
      <Tile label="All-time miles" value={stats.totalMilesAllTime.toFixed(1)} />
      <Tile
        label="Weekly streak"
        value={String(stats.weeklyStreak)}
        suffix={stats.weeklyStreak === 1 ? 'week' : 'weeks'}
      />
    </div>
  );
};

const Tile: React.FC<{ label: string; value: string; suffix?: string }> = ({
  label,
  value,
  suffix,
}) => (
  <div className="rounded-xl border border-gray-200 bg-white p-4">
    <p className="text-xs font-medium uppercase tracking-wide text-gray-400">{label}</p>
    <p className="mt-1 flex items-baseline gap-1">
      <span className="text-2xl font-bold text-gray-900">{value}</span>
      {suffix && <span className="text-sm text-gray-500">{suffix}</span>}
    </p>
  </div>
);
