// frontend/src/components/StravaCredentialsForm.tsx
// Onboarding + settings UI for the user's own Strava API application.
//
// Why the user has to do this at all: a free Strava API application has an athlete capacity of
// 1. If every RunSync user shared one application, the whole product would be capped at a single
// athlete. Instead each user registers their own free app — they are the only athlete on it —
// and RunSync stays free no matter how many people use it.
//
// The setup is the highest drop-off point in the product, so the steps are numbered, the exact
// value to paste into Strava is shown with a copy button, and every field links straight to the
// page it refers to.
//
// Security note: the client secret is write-only. Once saved, the backend never returns it, so
// this form shows a masked placeholder and requires a fresh paste to change it — the same way a
// password field behaves. The secret is only ever sent over HTTPS to PUT /api/strava/credentials.
//
// → Calls: frontend/src/api/stravaApi.ts → getStravaCredentialStatus(), saveStravaCredentials(),
//          deleteStravaCredentials()
// → Types: frontend/src/types/strava.ts → StravaCredentialStatus

import React, { useEffect, useState } from 'react';
import {
  deleteStravaCredentials,
  getStravaCredentialStatus,
  saveStravaCredentials,
} from '../api/stravaApi';
import type { StravaCredentialStatus } from '../types/strava';

interface StravaCredentialsFormProps {
  // Called after credentials are saved or removed, so the parent can refresh connection state.
  onChange?: (status: StravaCredentialStatus) => void;
}

const STRAVA_API_SETTINGS_URL = 'https://www.strava.com/settings/api';

export const StravaCredentialsForm: React.FC<StravaCredentialsFormProps> = ({ onChange }) => {
  const [status, setStatus] = useState<StravaCredentialStatus | null>(null);
  const [clientId, setClientId] = useState('');
  const [clientSecret, setClientSecret] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedMessage, setSavedMessage] = useState<string | null>(null);
  const [isEditing, setIsEditing] = useState(false);

  useEffect(() => {
    let cancelled = false;

    getStravaCredentialStatus()
      .then((result) => {
        if (cancelled) return;
        setStatus(result);
        setClientId(result.clientId ?? '');
        // Only drop straight into the form when there's nothing configured yet.
        setIsEditing(!result.isConfigured);
      })
      .catch((err: Error) => {
        if (!cancelled) setError(err.message);
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const handleSave = async (event: React.FormEvent) => {
    event.preventDefault();
    setError(null);
    setSavedMessage(null);
    setIsSaving(true);

    try {
      const result = await saveStravaCredentials({
        clientId: clientId.trim(),
        clientSecret: clientSecret.trim(),
      });

      setStatus(result);
      // Never retain the secret in component state once it's been sent.
      setClientSecret('');
      setIsEditing(false);
      setSavedMessage('Strava application saved. You can connect your account now.');
      onChange?.(result);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setIsSaving(false);
    }
  };

  const handleRemove = async () => {
    if (
      !confirm(
        'Remove your Strava application? This also disconnects your Strava account. ' +
          'Your synced activities are kept.'
      )
    ) {
      return;
    }

    setError(null);
    setSavedMessage(null);
    setIsSaving(true);

    try {
      await deleteStravaCredentials();
      const refreshed = await getStravaCredentialStatus();
      setStatus(refreshed);
      setClientId('');
      setClientSecret('');
      setIsEditing(true);
      onChange?.(refreshed);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setIsSaving(false);
    }
  };

  if (isLoading) {
    return (
      <div className="rounded-xl border border-gray-200 p-6">
        <div className="h-4 w-48 animate-pulse rounded bg-gray-200" />
      </div>
    );
  }

  return (
    <section className="rounded-xl border border-gray-200 bg-white p-6">
      <header className="mb-4">
        <h2 className="text-lg font-semibold text-gray-900">Your Strava application</h2>
        <p className="mt-1 text-sm text-gray-600">
          Strava gives each free API application a limit of one athlete, so RunSync asks you to
          create your own. It takes about a minute and keeps RunSync free.
        </p>
      </header>

      {status?.isConfigured && !isEditing ? (
        <ConfiguredSummary
          status={status}
          isBusy={isSaving}
          onEdit={() => {
            setIsEditing(true);
            setSavedMessage(null);
          }}
          onRemove={handleRemove}
        />
      ) : (
        <form onSubmit={handleSave} className="space-y-5">
          <SetupSteps callbackDomain={status?.callbackDomain ?? ''} />

          <div>
            <label htmlFor="strava-client-id" className="block text-sm font-medium text-gray-700">
              Client ID
            </label>
            <input
              id="strava-client-id"
              type="text"
              inputMode="numeric"
              autoComplete="off"
              value={clientId}
              onChange={(e) => setClientId(e.target.value)}
              placeholder="123456"
              required
              className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-orange-500 focus:outline-none focus:ring-1 focus:ring-orange-500"
            />
          </div>

          <div>
            <label htmlFor="strava-client-secret" className="block text-sm font-medium text-gray-700">
              Client Secret
            </label>
            <input
              id="strava-client-secret"
              // type=password keeps it out of shoulder-surfing range and stops browsers and
              // password managers from treating it as an ordinary autofillable text field.
              type="password"
              autoComplete="off"
              spellCheck={false}
              value={clientSecret}
              onChange={(e) => setClientSecret(e.target.value)}
              placeholder="40-character secret from your Strava app"
              required
              className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 font-mono text-sm focus:border-orange-500 focus:outline-none focus:ring-1 focus:ring-orange-500"
            />
            <p className="mt-1 text-xs text-gray-500">
              Encrypted before it is stored, and never shown again after saving.
            </p>
          </div>

          {error && (
            <p role="alert" className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
              {error}
            </p>
          )}

          <div className="flex items-center gap-3">
            <button
              type="submit"
              disabled={isSaving || !clientId.trim() || !clientSecret.trim()}
              className="rounded-lg bg-orange-500 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-orange-600 disabled:opacity-60"
            >
              {isSaving ? 'Saving…' : 'Save application'}
            </button>

            {status?.isConfigured && (
              <button
                type="button"
                onClick={() => {
                  setIsEditing(false);
                  setClientSecret('');
                  setClientId(status.clientId ?? '');
                  setError(null);
                }}
                className="text-sm text-gray-500 hover:text-gray-700"
              >
                Cancel
              </button>
            )}
          </div>
        </form>
      )}

      {savedMessage && !isEditing && (
        <p role="status" className="mt-4 rounded-lg bg-green-50 px-3 py-2 text-sm text-green-700">
          {savedMessage}
        </p>
      )}
    </section>
  );
};

// ── Sub-components ────────────────────────────────────────────────────────────

const ConfiguredSummary: React.FC<{
  status: StravaCredentialStatus;
  isBusy: boolean;
  onEdit: () => void;
  onRemove: () => void;
}> = ({ status, isBusy, onEdit, onRemove }) => (
  <div className="space-y-4">
    <dl className="space-y-2 text-sm">
      <div className="flex justify-between gap-4">
        <dt className="text-gray-500">Client ID</dt>
        <dd className="font-mono text-gray-900">{status.clientId}</dd>
      </div>
      <div className="flex justify-between gap-4">
        <dt className="text-gray-500">Client Secret</dt>
        <dd className="font-mono text-gray-400">••••••••••••••••</dd>
      </div>
      {status.updatedAt && (
        <div className="flex justify-between gap-4">
          <dt className="text-gray-500">Last updated</dt>
          <dd className="text-gray-900">{new Date(status.updatedAt).toLocaleDateString()}</dd>
        </div>
      )}
    </dl>

    <div className="flex items-center gap-3">
      <button
        type="button"
        onClick={onEdit}
        disabled={isBusy}
        className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm font-medium text-gray-700 transition hover:bg-gray-50 disabled:opacity-60"
      >
        Replace credentials
      </button>
      <button
        type="button"
        onClick={onRemove}
        disabled={isBusy}
        className="text-sm text-gray-400 transition hover:text-red-600 disabled:opacity-40"
      >
        Remove
      </button>
    </div>
  </div>
);

const SetupSteps: React.FC<{ callbackDomain: string }> = ({ callbackDomain }) => (
  <ol className="space-y-3 rounded-lg bg-gray-50 p-4 text-sm text-gray-700">
    <li className="flex gap-3">
      <StepNumber>1</StepNumber>
      <span>
        Open{' '}
        <a
          href={STRAVA_API_SETTINGS_URL}
          target="_blank"
          rel="noopener noreferrer"
          className="font-medium text-orange-600 underline"
        >
          strava.com/settings/api
        </a>{' '}
        and create an application. Any name and category will do.
      </span>
    </li>
    <li className="flex gap-3">
      <StepNumber>2</StepNumber>
      <div className="min-w-0">
        <p>
          Set <span className="font-medium">Authorization Callback Domain</span> to exactly:
        </p>
        <CopyableValue value={callbackDomain} />
      </div>
    </li>
    <li className="flex gap-3">
      <StepNumber>3</StepNumber>
      <span>Copy the Client ID and Client Secret from that page into the fields below.</span>
    </li>
  </ol>
);

const StepNumber: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <span className="flex h-5 w-5 flex-none items-center justify-center rounded-full bg-orange-500 text-xs font-bold text-white">
    {children}
  </span>
);

const CopyableValue: React.FC<{ value: string }> = ({ value }) => {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard access can be blocked (insecure origin, permissions). The value is visible
      // on screen either way, so silently fall back to manual selection.
    }
  };

  if (!value) {
    return (
      <p className="mt-1 text-xs text-gray-500">
        (RunSync could not determine its callback domain — check the server configuration.)
      </p>
    );
  }

  return (
    <div className="mt-1 flex items-center gap-2">
      <code className="truncate rounded bg-white px-2 py-1 font-mono text-xs text-gray-900 ring-1 ring-gray-200">
        {value}
      </code>
      <button
        type="button"
        onClick={handleCopy}
        className="flex-none text-xs font-medium text-orange-600 hover:text-orange-700"
      >
        {copied ? 'Copied' : 'Copy'}
      </button>
    </div>
  );
};
