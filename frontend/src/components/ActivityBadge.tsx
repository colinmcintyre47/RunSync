// frontend/src/components/ActivityBadge.tsx
// Compact badge showing the key metrics from a completed Strava run.
// Displayed inside TrainingDay.tsx when a run has been matched to a plan day.
//
// Color coding:
//   Green  — actual miles >= planned miles (completed or exceeded)
//   Amber  — actual miles within 10% short of planned
//   Red    — actual miles more than 10% below planned
//
// → Receives data from TrainingDayActivity (matched run fields)
// → Used by: TrainingDay.tsx

import React from 'react';
import type { TrainingDayActivity } from '../types/strava';

interface ActivityBadgeProps {
  activity: TrainingDayActivity;
}

function getMileageColor(actual: number, planned: number): string {
  if (actual >= planned) return 'text-emerald-600';
  const ratio = actual / planned;
  return ratio >= 0.9 ? 'text-amber-500' : 'text-red-500';
}

export const ActivityBadge: React.FC<ActivityBadgeProps> = ({ activity }) => {
  const {
    actualMiles,
    plannedMiles,
    actualPace,
    averageHeartrate,
    elevationGainFeet,
    stravaActivityId,
    activityName,
  } = activity;

  if (actualMiles === null) return null;

  const mileageColor = getMileageColor(actualMiles, plannedMiles);

  return (
    <div className="mt-3 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2">
      {/* Activity name with Strava link */}
      <div className="mb-1.5 flex items-center justify-between">
        <span className="text-xs font-medium text-emerald-700">
          ✓ {activityName ?? 'Strava Run'}
        </span>
        {stravaActivityId !== null && (
          <a
            href={`https://www.strava.com/activities/${stravaActivityId}`}
            target="_blank"
            rel="noopener noreferrer"
            className="flex items-center gap-1 text-xs text-orange-500 hover:text-orange-600"
            aria-label="View on Strava"
          >
            <StravaLogo />
            <span>View</span>
          </a>
        )}
      </div>

      {/* Metrics row */}
      <div className="flex flex-wrap gap-x-3 gap-y-0.5 text-xs text-gray-600">
        <span className={`font-semibold ${mileageColor}`}>
          {actualMiles.toFixed(2)} mi
        </span>

        {actualPace && actualPace !== '--:-- /mi' && (
          <span>⏱ {actualPace}</span>
        )}

        {averageHeartrate !== null && averageHeartrate > 0 && (
          <span>♥ {Math.round(averageHeartrate)} bpm</span>
        )}

        {elevationGainFeet !== null && elevationGainFeet > 0 && (
          <span>↑ {Math.round(elevationGainFeet)} ft</span>
        )}
      </div>
    </div>
  );
};

// Inline Strava orange "S" logo mark — avoids external image dependency
function StravaLogo() {
  return (
    <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M15.387 17.944l-2.089-4.116h-3.065L15.387 24l5.15-10.172h-3.066m-7.008-5.599l2.836 5.598h4.172L10.463 0l-7 13.828h4.169" />
    </svg>
  );
}
