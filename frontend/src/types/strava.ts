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

// Mirrors RunActivityDto.cs — one synced Strava run with every field the backend exposes.
export interface RunActivity {
  stravaId: number;
  name: string;
  date: string;                    // ISO 8601 local start time
  miles: number;
  pace: string;                    // formatted "MM:SS /mi"
  avgHeartrate: number | null;
  maxHeartrate: number | null;
  elevationGainFeet: number;
  effortLevel: string;
  isManualEntry: boolean;
  sufferScore: number | null;
  averageCadence: number | null;   // steps/min
  workoutTypeLabel: string;        // "Race" | "Long Run" | "Workout" | "Run"
  sportType: string;               // "Run" | "TrailRun" | "VirtualRun"
  elapsedTimeSeconds: number;
  elapsedTime: string;             // formatted "H:MM:SS"
  movingTime: string;              // formatted "H:MM:SS"
  prCount: number;
  summaryPolyline: string;         // encoded route; excluded from the spreadsheet export
}

// Mirrors WeekSummaryDto.cs — runs grouped into Mon–Sun calendar weeks, newest week first.
export interface WeekSummary {
  weekLabel: string;               // e.g. "Week of May 19"
  weekStart: string;               // "YYYY-MM-DD" (serialized DateOnly)
  weekEnd: string;                 // "YYYY-MM-DD"
  totalMiles: number;
  runCount: number;
  runs: RunActivity[];
}

// Mirrors DashboardStatsDto.cs
export interface DashboardStats {
  milesThisWeek: number;
  runsThisWeek: number;
  totalMilesAllTime: number;
  weeklyStreak: number;            // consecutive Mon–Sun weeks containing at least one run
}

// Mirrors AthleteProfileDto.cs. Null until the user connects Strava and syncs.
export interface AthleteProfile {
  firstName: string;
  lastName: string;
  profileUrl: string;
  city: string;
  state: string;
  stravaAthleteId: number;
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
