// frontend/src/components/TrainingDay.tsx
// Card component representing a single day in the half marathon training plan.
// Shows the planned workout on the left, and the actual Strava activity on the right (if matched).
//
// Visual states:
//   Completed   — green border, ActivityBadge showing actual stats below plan details
//   Today       — blue ring highlight so the current day stands out
//   Rest day    — muted styling, no mileage
//   Future      — normal styling
//   Past/missed — slightly dimmed if no activity logged
//
// → activity data comes from TrainingDayActivity (backend DTO)
// → ActivityBadge.tsx renders the matched Strava run metrics
// → dateUtils.ts provides isToday() and isPastDay()

import React from 'react';
import { ActivityBadge } from './ActivityBadge';
import type { TrainingDayActivity } from '../types/strava';
import { formatPlanDate, isPastDay, isToday } from '../utils/dateUtils';

interface TrainingDayProps {
  activity: TrainingDayActivity;
}

const WORKOUT_COLORS: Record<string, string> = {
  'Easy Run': 'bg-sky-100 text-sky-700',
  'Tempo Run': 'bg-orange-100 text-orange-700',
  'Intervals': 'bg-red-100 text-red-700',
  'Long Run': 'bg-purple-100 text-purple-700',
  'Race Pace Run': 'bg-amber-100 text-amber-700',
  'Shakeout Run': 'bg-teal-100 text-teal-700',
  'Race Day': 'bg-yellow-100 text-yellow-800',
  'Rest': 'bg-gray-100 text-gray-500',
};

function getWorkoutBadgeClass(workoutType: string): string {
  return WORKOUT_COLORS[workoutType] ?? 'bg-gray-100 text-gray-600';
}

export const TrainingDay: React.FC<TrainingDayProps> = ({ activity }) => {
  const {
    planDate,
    dayLabel,
    workoutType,
    plannedMiles,
    heartRateZone,
    notes,
    isCompleted,
  } = activity;

  const today = isToday(planDate);
  const past = isPastDay(planDate);
  const isRest = workoutType === 'Rest';
  const missed = past && !isCompleted && !isRest;

  const cardClasses = [
    'rounded-xl border p-4 transition-all',
    today ? 'border-blue-400 ring-2 ring-blue-100' : 'border-gray-200',
    isCompleted ? 'bg-white' : missed ? 'bg-gray-50 opacity-80' : 'bg-white',
  ].join(' ');

  return (
    <div className={cardClasses}>
      {/* ── Header: date + workout type badge ─────────────────────────── */}
      <div className="mb-2 flex items-start justify-between gap-2">
        <div>
          <p className="text-xs font-medium text-gray-400">{dayLabel}</p>
          <p className="text-sm font-semibold text-gray-800">{formatPlanDate(planDate)}</p>
        </div>
        <span
          className={`inline-flex shrink-0 items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${getWorkoutBadgeClass(workoutType)}`}
        >
          {workoutType}
        </span>
      </div>

      {/* ── Planned workout details ────────────────────────────────────── */}
      {!isRest && (
        <div className="mb-1 flex items-baseline gap-2">
          <span className="text-xl font-bold text-gray-900">{plannedMiles}</span>
          <span className="text-sm text-gray-500">miles planned</span>
        </div>
      )}

      <p className="text-xs text-gray-400">{heartRateZone}</p>

      {notes && (
        <p className="mt-1.5 text-xs leading-relaxed text-gray-500">{notes}</p>
      )}

      {/* ── Actual Strava activity (if matched) ───────────────────────── */}
      {isCompleted ? (
        <ActivityBadge activity={activity} />
      ) : (
        !isRest && past && (
          <p className="mt-3 text-xs italic text-gray-300">No activity logged</p>
        )
      )}

      {/* Today indicator */}
      {today && (
        <div className="mt-2 text-xs font-medium text-blue-500">← Today</div>
      )}
    </div>
  );
};
