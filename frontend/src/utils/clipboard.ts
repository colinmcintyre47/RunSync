// frontend/src/utils/clipboard.ts
// Clipboard write with a fallback path.
//
// navigator.clipboard is only available in a secure context (HTTPS or localhost) and can be
// blocked by permissions policy. Deployed RunSync is HTTPS so the modern path normally works,
// but the fallback keeps "Copy all data" functional when someone runs the frontend over plain
// HTTP on their LAN — which is exactly how people demo this to a friend.
//
// → Used by: CopyAllDataButton.tsx, StravaCredentialsForm.tsx

/**
 * Copies text to the clipboard. Returns true on success, false if every method failed
 * (callers should surface a "select and copy manually" message rather than failing silently).
 */
export async function copyToClipboard(text: string): Promise<boolean> {
  // Preferred path: async Clipboard API.
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Fall through — permissions policy or insecure origin.
    }
  }

  return copyViaHiddenTextarea(text);
}

/**
 * Legacy execCommand path. Deprecated, but it is the only thing that works on insecure
 * origins, and it degrades to returning false rather than throwing.
 */
function copyViaHiddenTextarea(text: string): boolean {
  const textarea = document.createElement('textarea');
  textarea.value = text;

  // Keep it off-screen and non-disruptive: no scroll jump, no focus ring, no zoom on iOS.
  textarea.setAttribute('readonly', '');
  textarea.style.position = 'fixed';
  textarea.style.top = '-9999px';
  textarea.style.left = '-9999px';
  textarea.style.opacity = '0';

  document.body.appendChild(textarea);

  try {
    textarea.select();
    // iOS Safari ignores select() on readonly fields unless the range is set explicitly.
    textarea.setSelectionRange(0, textarea.value.length);
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    document.body.removeChild(textarea);
  }
}
