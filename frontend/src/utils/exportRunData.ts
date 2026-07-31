// frontend/src/utils/exportRunData.ts
// Turns the synced run data into the two formats the "Copy all data" button offers.
//
// TSV rather than CSV for the spreadsheet export: run names routinely contain commas
// ("Tuesday 6mi, easy"), which would need quoting and escaping in CSV and which some
// spreadsheet importers still get wrong. Tabs are far rarer in run names, and pasting TSV
// straight into Google Sheets or Excel lands each field in its own column with no import
// dialog at all.
//
// The TSV omits summaryPolyline — an encoded route is hundreds of characters and would make
// every row unreadable. The JSON export includes it, so nothing is actually lost.
//
// → Used by: CopyAllDataButton.tsx

import type { AthleteProfile, DashboardStats, RunActivity, WeekSummary } from '../types/strava';

export interface RunDataExport {
  profile: AthleteProfile | null;
  stats: DashboardStats | null;
  weeks: WeekSummary[];
}

const TSV_COLUMNS = [
  'Date',
  'Week',
  'Name',
  'Sport Type',
  'Workout Type',
  'Miles',
  'Moving Time',
  'Elapsed Time',
  'Pace (/mi)',
  'Avg HR',
  'Max HR',
  'Elevation Gain (ft)',
  'Cadence (spm)',
  'Suffer Score',
  'Effort',
  'PRs',
  'Manual Entry',
  'Strava ID',
  'Strava URL',
] as const;

/**
 * Strips characters that would break the row/column structure. A literal tab or newline
 * inside a run name would otherwise shift every subsequent column in that row.
 */
function tsvCell(value: string | number | boolean | null | undefined): string {
  if (value === null || value === undefined) return '';
  return String(value).replace(/[\t\r\n]+/g, ' ').trim();
}

function formatDate(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;

  // YYYY-MM-DD HH:MM in local time — sorts correctly as text in a spreadsheet.
  const pad = (n: number) => String(n).padStart(2, '0');
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    ` ${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}

function toRow(run: RunActivity, weekLabel: string): string {
  return [
    formatDate(run.date),
    weekLabel,
    run.name,
    run.sportType,
    run.workoutTypeLabel,
    run.miles,
    run.movingTime,
    run.elapsedTime,
    run.pace,
    run.avgHeartrate,
    run.maxHeartrate,
    run.elevationGainFeet,
    run.averageCadence,
    run.sufferScore,
    run.effortLevel,
    run.prCount,
    run.isManualEntry ? 'yes' : 'no',
    run.stravaId,
    `https://www.strava.com/activities/${run.stravaId}`,
  ]
    .map(tsvCell)
    .join('\t');
}

/**
 * Spreadsheet-ready export: a header row plus one row per run, newest first.
 * Paste directly into Google Sheets or Excel.
 */
export function buildTsv({ weeks }: RunDataExport): string {
  const rows = weeks.flatMap((week) => week.runs.map((run) => toRow(run, week.weekLabel)));
  return [TSV_COLUMNS.join('\t'), ...rows].join('\n');
}

/**
 * Complete export including the athlete profile, summary stats, and every field on every run
 * (summaryPolyline included). Use this when the data is going into another tool.
 */
export function buildJson({ profile, stats, weeks }: RunDataExport): string {
  return JSON.stringify(
    {
      exportedAt: new Date().toISOString(),
      source: 'RunSync',
      athlete: profile,
      stats,
      totalRuns: countRuns(weeks),
      weeks,
    },
    null,
    2
  );
}

export function countRuns(weeks: WeekSummary[]): number {
  return weeks.reduce((total, week) => total + week.runs.length, 0);
}
