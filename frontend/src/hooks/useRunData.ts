// frontend/src/hooks/useRunData.ts
// Fetches everything derived from the user's synced Strava activities: the full weekly run
// log, the summary stats, and the athlete profile.
//
// Kept separate from useStravaActivities (which owns the fixed training-plan view) because
// these three endpoints work for any user regardless of whether they have a training goal
// configured, and because the Copy All Data export is built from exactly this data.
//
// All three are fetched in parallel and share one loading flag — they always render together.
//
// → Calls: frontend/src/api/stravaApi.ts → getWeeklyLog(), getDashboardStats(), getAthleteProfile()
// → Consumed by: frontend/src/components/Dashboard.tsx

import { useCallback, useEffect, useState } from 'react';
import { getAthleteProfile, getDashboardStats, getWeeklyLog } from '../api/stravaApi';
import type { AthleteProfile, DashboardStats, WeekSummary } from '../types/strava';

interface UseRunDataResult {
  weeks: WeekSummary[];
  stats: DashboardStats | null;
  profile: AthleteProfile | null;
  isLoading: boolean;
  error: string | null;
  refresh: () => Promise<void>;
}

export function useRunData(): UseRunDataResult {
  const [weeks, setWeeks] = useState<WeekSummary[]>([]);
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [profile, setProfile] = useState<AthleteProfile | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    setError(null);
    try {
      const [weeklyLog, dashboardStats, athleteProfile] = await Promise.all([
        getWeeklyLog(),
        getDashboardStats(),
        getAthleteProfile(),
      ]);

      setWeeks(weeklyLog);
      setStats(dashboardStats);
      setProfile(athleteProfile);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load your run data.');
    }
  }, []);

  useEffect(() => {
    setIsLoading(true);
    fetchData().finally(() => setIsLoading(false));
  }, [fetchData]);

  return { weeks, stats, profile, isLoading, error, refresh: fetchData };
}
