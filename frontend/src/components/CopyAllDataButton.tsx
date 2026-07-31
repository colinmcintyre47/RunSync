// frontend/src/components/CopyAllDataButton.tsx
// "Copy all data" — puts every synced run on the clipboard.
//
// Primary action copies spreadsheet-ready TSV, because pasting into Google Sheets or Excel is
// what people actually want to do with their own training data. The secondary "as JSON" link
// copies the complete structured export (including route polylines and summary stats) for
// anyone feeding it into another tool.
//
// The button reports what it did — "Copied 143 runs" — rather than a bare checkmark, so the
// user knows the export wasn't silently empty.
//
// → Data shaping: frontend/src/utils/exportRunData.ts
// → Clipboard + fallback: frontend/src/utils/clipboard.ts

import React, { useState } from 'react';
import { copyToClipboard } from '../utils/clipboard';
import { buildJson, buildTsv, countRuns, type RunDataExport } from '../utils/exportRunData';

interface CopyAllDataButtonProps {
  data: RunDataExport;
  disabled?: boolean;
}

type Feedback = { text: string; tone: 'success' | 'error' };

export const CopyAllDataButton: React.FC<CopyAllDataButtonProps> = ({ data, disabled }) => {
  const [feedback, setFeedback] = useState<Feedback | null>(null);

  const runCount = countRuns(data.weeks);
  const isEmpty = runCount === 0;

  const copy = async (format: 'tsv' | 'json') => {
    const text = format === 'tsv' ? buildTsv(data) : buildJson(data);
    const succeeded = await copyToClipboard(text);

    setFeedback(
      succeeded
        ? {
            text: `Copied ${runCount} run${runCount === 1 ? '' : 's'}${
              format === 'json' ? ' as JSON' : ''
            }`,
            tone: 'success',
          }
        : {
            text: "Your browser blocked clipboard access — try again, or use your browser's copy shortcut.",
            tone: 'error',
          }
    );

    // Clear the confirmation so the control returns to its resting state.
    window.setTimeout(() => setFeedback(null), 4000);
  };

  return (
    <div className="flex flex-wrap items-center gap-3">
      <button
        type="button"
        onClick={() => copy('tsv')}
        disabled={disabled || isEmpty}
        title={
          isEmpty
            ? 'Sync your Strava activities first'
            : 'Copy every run as spreadsheet columns — paste into Google Sheets or Excel'
        }
        className="flex items-center gap-2 rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-700 shadow-sm transition hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
      >
        <ClipboardIcon />
        Copy all data
      </button>

      <button
        type="button"
        onClick={() => copy('json')}
        disabled={disabled || isEmpty}
        title="Copy the complete export including route data and summary stats"
        className="text-xs font-medium text-gray-500 underline transition hover:text-gray-700 disabled:cursor-not-allowed disabled:opacity-50"
      >
        as JSON
      </button>

      {feedback && (
        <span
          role="status"
          className={
            feedback.tone === 'success'
              ? 'text-xs font-medium text-emerald-600'
              : 'text-xs font-medium text-red-600'
          }
        >
          {feedback.text}
        </span>
      )}
    </div>
  );
};

function ClipboardIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <rect x="9" y="9" width="13" height="13" rx="2" ry="2" />
      <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
    </svg>
  );
}
