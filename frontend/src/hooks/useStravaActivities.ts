// frontend/src/hooks/useStravaActivities.ts
// Custom React hook that fetches the training plan with matched Strava activities
// and manages loading, error, and sync state.
//
// Fetches both the training plan and sync status on mount.
// Re-fetches automatically after a manual sync is triggered.
//
// → Calls: frontend/src/api/stravaApi.ts → getTrainingPlan(), getSyncStatus(), triggerSync()
// → Returns data shaped for: frontend/src/components/TrainingDay.tsx

import { useCallback, useEffect, useState } from 'react';
import * as stravaApi from '../api/stravaApi';
import type { SyncStatus, TrainingDayActivity } from '../types/strava';

interface UseStravaActivitiesResult {
  days: TrainingDayActivity[];
  syncStatus: SyncStatus | null;
  isLoading: boolean;
  isSyncing: boolean;
  error: string | null;
  triggerSync: () => Promise<void>;
  refresh: () => Promise<void>;
}

export function useStravaActivities(): UseStravaActivitiesResult {
  const [days, setDays] = useState<TrainingDayActivity[]>([]);
  const [syncStatus, setSyncStatus] = useState<SyncStatus | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isSyncing, setIsSyncing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    setError(null);
    try {
      // Fetch both in parallel — they're independent requests
      const [planData, statusData] = await Promise.all([
        stravaApi.getTrainingPlan(),
        stravaApi.getSyncStatus(),
      ]);
      setDays(planData);
      setSyncStatus(statusData);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load training data.');
    }
  }, []);

  // Load on mount
  useEffect(() => {
    setIsLoading(true);
    fetchData().finally(() => setIsLoading(false));
  }, [fetchData]);

  const triggerSync = useCallback(async () => {
    setIsSyncing(true);
    setError(null);
    try {
      await stravaApi.triggerSync();
      // Re-fetch after sync so the UI reflects the newly pulled activities
      await fetchData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Sync failed. Please try again.');
    } finally {
      setIsSyncing(false);
    }
  }, [fetchData]);

  return {
    days,
    syncStatus,
    isLoading,
    isSyncing,
    error,
    triggerSync,
    refresh: fetchData,
  };
}
