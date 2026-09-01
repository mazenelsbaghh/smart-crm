import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

// Node exposes an incomplete localStorage shim when its storage file flag is unset.
// Keep this browser-storage boundary deterministic for component tests.
const storageValues = new Map<string, string>();
const testLocalStorage: Storage = {
  get length() { return storageValues.size; },
  clear: () => storageValues.clear(),
  getItem: (key) => storageValues.get(key) ?? null,
  key: (index) => Array.from(storageValues.keys())[index] ?? null,
  removeItem: (key) => { storageValues.delete(key); },
  setItem: (key, value) => { storageValues.set(key, String(value)); },
};
Object.defineProperty(globalThis, 'localStorage', {
  configurable: true,
  value: testLocalStorage,
});
Object.defineProperty(window, 'localStorage', { configurable: true, value: testLocalStorage });

afterEach(() => cleanup());
