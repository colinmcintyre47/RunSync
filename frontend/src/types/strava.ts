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

// Mirrors StravaCredentialStatusDto.cs
//
// Each user registers their own free Strava API application (a free app supports exactly one
// athlete — themselves), so RunSync isn't capped by a single shared app's athlete limit.
//
// Note there is no clientSecret field, and there must never be one: the backend encrypts the
// secret on save and never returns it. The UI shows a masked placeholder once configured and
// requires a fresh paste to change it — same posture as a password.
export interface StravaCredentialStatus {
  isConfigured: boolean;
  clientId: string | null;
  // The bare domain the user must enter as "Authorization Callback Domain" on Strava.
  callbackDomain: string;
  // The full callback URL, shown for reference.
  redirectUri: string;
  updatedAt: string | null;  // ISO 8601, or null if never configured
}

// Request body for saving credentials. Write-only — never returned by the API.
export interface StravaCredentialInput {
  clientId: string;
  clientSecret: string;
}
