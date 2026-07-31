// frontend/src/components/SettingsPage.tsx
// Settings view. Currently wraps the Strava API application form, which is the only
// per-user configuration RunSync needs.
//
// → Children: StravaCredentialsForm.tsx
// → Rendered by: frontend/src/App.tsx

import React from 'react';
import { StravaCredentialsForm } from './StravaCredentialsForm';
import type { StravaCredentialStatus } from '../types/strava';

interface SettingsPageProps {
  onBack: () => void;
  onCredentialsChange: (status: StravaCredentialStatus) => void;
}

export const SettingsPage: React.FC<SettingsPageProps> = ({ onBack, onCredentialsChange }) => (
  <div className="mx-auto max-w-2xl px-4 py-8">
    <header className="mb-6">
      <button
        onClick={onBack}
        className="text-sm text-gray-500 transition hover:text-gray-700"
      >
        ← Back to training plan
      </button>
      <h1 className="mt-2 text-2xl font-bold tracking-tight text-gray-900">Settings</h1>
    </header>

    <StravaCredentialsForm onChange={onCredentialsChange} />
  </div>
);
