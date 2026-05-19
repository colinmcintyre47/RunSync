// frontend/src/types/strava.ts
// TypeScript interfaces that mirror the RunSync backend DTOs exactly.
// If a backend DTO property changes, update the matching interface here.
//
// → TrainingDayActivity mirrors TrainingDayActivityDto.cs
// → SyncStatus mirrors the anonymous object returned by ActivitiesController → GetSyncStatus()
// → Used by: useStravaActivities.ts, TrainingDay.tsx, ActivityBadge.tsx, SyncStatus.tsx

export interface TrainingDayActivity {
  // ── Training Plan Side (always populated) ──────────────────────────────────
  planDate: string;           // ISO 8601 date string, e.g. "2026-02-02T00:00:00"
  dayLabel: string;           // e.g. "Week 6 · Tuesday"
  workoutType: string;        // e.g. "Easy Run", "Tempo Run", "Long Run", "Rest"
  plannedMiles: number;
  heartRateZone: string;      // e.g. "Zone 2 (aerobic)"
  notes: string;

  // ── Actual Strava Activity Side (null if no activity logged that day) ───────
  stravaActivityId: number | null;
  activityName: string | null;
  actualMiles: number | null;
  actualPace: string | null;  // Formatted "MM:SS /mi" from backend
  averageHeartrate: number | null;
  elevationGainFeet: number | null;
  isCompleted: boolean;
}

export interface SyncStatus {
  lastSyncedAt: string | null;  // ISO 8601 or null if never synced
  totalActivities: number;
  isConnected: boolean;
}

export interface AuthResponse {
  token: string;
  displayName: string;
  email: string;
}
