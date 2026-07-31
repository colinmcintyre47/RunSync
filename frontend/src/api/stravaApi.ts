// frontend/src/api/stravaApi.ts
// All HTTP calls to the RunSync backend are centralized here.
// Components never call fetch() directly — they always go through this module.
//
// Auth: the JWT stored in localStorage is attached as "Authorization: Bearer <token>"
// on every authenticated request. If the token is missing, the backend returns 401.
//
// Base URL: read from VITE_API_BASE_URL environment variable (set in Netlify dashboard).
// For local dev, create a .env.local file with: VITE_API_BASE_URL=http://localhost:5000
//
// → Types defined in: frontend/src/types/strava.ts
// → Consumed by: frontend/src/hooks/useStravaActivities.ts

import type {
  AthleteProfile,
  AuthResponse,
  DashboardStats,
  StravaCredentialInput,
  StravaCredentialStatus,
  SyncStatus,
  TrainingDayActivity,
  WeekSummary,
} from '../types/strava';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL as string;

// ── Auth token storage ────────────────────────────────────────────────────────
// localStorage rather than a cookie because the API is on a different origin and
// authenticates with a Bearer token. The session survives a page reload, which the
// Strava OAuth round trip depends on — the user leaves the site entirely and comes
// back via a redirect from Strava.

const TOKEN_KEY = 'runsync_token';
const DISPLAY_NAME_KEY = 'runsync_display_name';

export function storeAuth(auth: AuthResponse): void {
  localStorage.setItem(TOKEN_KEY, auth.token);
  localStorage.setItem(DISPLAY_NAME_KEY, auth.displayName);
}

export function clearAuth(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(DISPLAY_NAME_KEY);
}

/** Returns the stored session, or null if the user isn't signed in. */
export function getStoredAuth(): { token: string; displayName: string } | null {
  const token = localStorage.getItem(TOKEN_KEY);
  if (!token) return null;
  return { token, displayName: localStorage.getItem(DISPLAY_NAME_KEY) ?? '' };
}

// ── Auth helpers ──────────────────────────────────────────────────────────────

function getAuthHeaders(): HeadersInit {
  const token = localStorage.getItem(TOKEN_KEY);
  return token
    ? { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }
    : { 'Content-Type': 'application/json' };
}

// Two different error shapes can come back:
//   - ExceptionHandlingMiddleware  → { statusCode, message, traceId }
//   - ASP.NET [ApiController] model validation → ProblemDetails with { errors: { Field: [...] } }
// Without the ProblemDetails branch, a bad Client ID would surface as a useless "HTTP 400".
function extractErrorMessage(body: unknown, status: number): string {
  if (typeof body === 'object' && body !== null) {
    const problem = body as { message?: string; errors?: Record<string, string[]> };

    if (problem.errors) {
      const messages = Object.values(problem.errors).flat().filter(Boolean);
      if (messages.length > 0) return messages.join(' ');
    }

    if (problem.message) return problem.message;
  }

  return `HTTP ${status}`;
}

/**
 * Error carrying the HTTP status alongside the message, so callers can react to specific
 * failures — App.tsx signs the user out on a 401 rather than showing "HTTP 401" forever.
 */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

async function throwApiError(response: Response, fallback: string): Promise<never> {
  const body = await response.json().catch(() => null);
  const message = body === null ? fallback : extractErrorMessage(body, response.status);
  throw new ApiError(message, response.status);
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    await throwApiError(response, `HTTP ${response.status}`);
  }
  return response.json() as Promise<T>;
}

// ── Auth endpoints ────────────────────────────────────────────────────────────

export async function login(email: string, password: string): Promise<AuthResponse> {
  const response = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  return handleResponse<AuthResponse>(response);
}

export async function register(
  email: string,
  password: string,
  displayName: string
): Promise<AuthResponse> {
  const response = await fetch(`${API_BASE_URL}/api/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password, displayName }),
  });
  return handleResponse<AuthResponse>(response);
}

// ── Training plan endpoints ───────────────────────────────────────────────────

export async function getTrainingPlan(): Promise<TrainingDayActivity[]> {
  const response = await fetch(`${API_BASE_URL}/api/activities/training-plan`, {
    headers: getAuthHeaders(),
  });
  return handleResponse<TrainingDayActivity[]>(response);
}

export async function getSyncStatus(): Promise<SyncStatus> {
  const response = await fetch(`${API_BASE_URL}/api/activities/sync-status`, {
    headers: getAuthHeaders(),
  });
  return handleResponse<SyncStatus>(response);
}

// ── Synced run data ───────────────────────────────────────────────────────────

/** Every synced run, grouped into Mon–Sun weeks, newest week first. */
export async function getWeeklyLog(): Promise<WeekSummary[]> {
  const response = await fetch(`${API_BASE_URL}/api/activities/weekly-log`, {
    headers: getAuthHeaders(),
  });
  return handleResponse<WeekSummary[]>(response);
}

export async function getDashboardStats(): Promise<DashboardStats> {
  const response = await fetch(`${API_BASE_URL}/api/activities/dashboard-stats`, {
    headers: getAuthHeaders(),
  });
  return handleResponse<DashboardStats>(response);
}

/**
 * The Strava athlete profile captured during OAuth.
 * Returns null when the endpoint replies 204 — the user hasn't connected Strava yet.
 */
export async function getAthleteProfile(): Promise<AthleteProfile | null> {
  const response = await fetch(`${API_BASE_URL}/api/activities/athlete-profile`, {
    headers: getAuthHeaders(),
  });

  if (!response.ok) {
    await throwApiError(response, `HTTP ${response.status}`);
  }

  // 204 has an empty body — calling .json() on it would throw.
  if (response.status === 204) return null;

  return response.json() as Promise<AthleteProfile>;
}

// ── Strava API application credentials ────────────────────────────────────────
//
// Every user brings their own Strava API application. These three calls manage it.
// The client secret travels in exactly one direction — into saveStravaCredentials() — and
// is never returned by any endpoint.

export async function getStravaCredentialStatus(): Promise<StravaCredentialStatus> {
  const response = await fetch(`${API_BASE_URL}/api/strava/credentials`, {
    headers: getAuthHeaders(),
  });
  return handleResponse<StravaCredentialStatus>(response);
}

export async function saveStravaCredentials(
  input: StravaCredentialInput
): Promise<StravaCredentialStatus> {
  const response = await fetch(`${API_BASE_URL}/api/strava/credentials`, {
    method: 'PUT',
    headers: getAuthHeaders(),
    body: JSON.stringify(input),
  });
  return handleResponse<StravaCredentialStatus>(response);
}

export async function deleteStravaCredentials(): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/strava/credentials`, {
    method: 'DELETE',
    headers: getAuthHeaders(),
  });
  if (!response.ok) {
    await throwApiError(response, 'Failed to remove credentials');
  }
}

// ── Strava endpoints ──────────────────────────────────────────────────────────

export async function getStravaAuthUrl(): Promise<string> {
  const response = await fetch(`${API_BASE_URL}/api/strava/authorize`, {
    headers: getAuthHeaders(),
  });
  const data = await handleResponse<{ url: string }>(response);
  return data.url;
}

export async function triggerSync(): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/strava/sync`, {
    method: 'POST',
    headers: getAuthHeaders(),
  });
  if (!response.ok) {
    await throwApiError(response, 'Sync failed');
  }
}

export async function disconnectStrava(): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/strava/disconnect`, {
    method: 'DELETE',
    headers: getAuthHeaders(),
  });
  if (!response.ok) {
    await throwApiError(response, 'Disconnect failed');
  }
}
