// frontend/src/utils/dateUtils.ts
// Date formatting utilities used across the training plan UI.
// Centralizing these avoids inconsistent date formatting scattered across components.

/**
 * Formats an ISO date string as a short human-readable label.
 * e.g. "2026-02-02T00:00:00" → "Mon, Feb 2"
 */
export function formatPlanDate(isoDate: string): string {
  const date = new Date(isoDate);
  return date.toLocaleDateString('en-US', {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
  });
}

/**
 * Returns a relative time string for the last sync timestamp.
 * e.g. "2 minutes ago", "1 hour ago", "3 days ago"
 * Returns "Never" for null input.
 */
export function formatLastSynced(isoDate: string | null): string {
  if (!isoDate) return 'Never';

  const synced = new Date(isoDate);
  const now = new Date();
  const diffSeconds = Math.floor((now.getTime() - synced.getTime()) / 1000);

  if (diffSeconds < 60) return 'Just now';
  if (diffSeconds < 3600) {
    const mins = Math.floor(diffSeconds / 60);
    return `${mins} minute${mins !== 1 ? 's' : ''} ago`;
  }
  if (diffSeconds < 86400) {
    const hours = Math.floor(diffSeconds / 3600);
    return `${hours} hour${hours !== 1 ? 's' : ''} ago`;
  }

  const days = Math.floor(diffSeconds / 86400);
  return `${days} day${days !== 1 ? 's' : ''} ago`;
}

/**
 * Returns true if the plan date is today in the user's local timezone.
 * Used to highlight the current training day in the UI.
 */
export function isToday(isoDate: string): boolean {
  const planDate = new Date(isoDate);
  const today = new Date();
  return (
    planDate.getFullYear() === today.getFullYear() &&
    planDate.getMonth() === today.getMonth() &&
    planDate.getDate() === today.getDate()
  );
}

/**
 * Returns true if the plan date is in the past (before today).
 */
export function isPastDay(isoDate: string): boolean {
  const planDate = new Date(isoDate);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return planDate < today;
}
