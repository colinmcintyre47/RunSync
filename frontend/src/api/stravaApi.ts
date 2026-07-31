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
  AuthResponse,
  StravaCredentialInput,
  StravaCredentialStatus,
  SyncStatus,
  TrainingDayActivity,
} from '../types/strava';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL as string;

// ── Auth helpers ──────────────────────────────────────────────────────────────

function getAuthHeaders(): HeadersInit {
  const token = localStorage.getItem('runsync_token');
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

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(extractErrorMessage(body, response.status));
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
    const error = await response.json().catch(() => ({ message: 'Failed to remove credentials' }));
    throw new Error(error.message ?? `HTTP ${response.status}`);
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
    const error = await response.json().catch(() => ({ message: 'Sync failed' }));
    throw new Error(error.message ?? `HTTP ${response.status}`);
  }
}

export async function disconnectStrava(): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/strava/disconnect`, {
    method: 'DELETE',
    headers: getAuthHeaders(),
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({ message: 'Disconnect failed' }));
    throw new Error(error.message ?? `HTTP ${response.status}`);
  }
}
