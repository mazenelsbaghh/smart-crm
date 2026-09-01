import type { KeyboardEvent } from 'react';

const focusableSelector = [
  'button:not(:disabled)',
  'textarea:not(:disabled)',
  'input:not(:disabled)',
  'select:not(:disabled)',
  'a[href]',
].join(',');

export function handleDialogKeyDown(event: KeyboardEvent<HTMLElement>, onClose: () => void) {
  if (event.key === 'Escape') {
    event.preventDefault();
    onClose();
    return;
  }
  if (event.key !== 'Tab') return;

  const focusable = Array.from(event.currentTarget.querySelectorAll<HTMLElement>(focusableSelector));
  if (focusable.length === 0) return;

  const first = focusable[0];
  const last = focusable[focusable.length - 1];
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}
