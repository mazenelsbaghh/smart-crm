'use client';

import { useEffect, useSyncExternalStore, type ReactNode } from 'react';

const themeStorageKey = 'smart-customer-theme-v2';
const themeChangeEvent = 'smart-customer-theme-change';

function subscribeToTheme(onChange: () => void) {
  window.addEventListener('storage', onChange);
  window.addEventListener(themeChangeEvent, onChange);
  return () => {
    window.removeEventListener('storage', onChange);
    window.removeEventListener(themeChangeEvent, onChange);
  };
}

export function useTheme() {
  const isLight = useSyncExternalStore(subscribeToTheme,
    () => window.localStorage.getItem(themeStorageKey) !== 'dark', () => true);
  const toggleTheme = () => {
    window.localStorage.setItem(themeStorageKey, isLight ? 'dark' : 'light');
    window.dispatchEvent(new Event(themeChangeEvent));
  };
  return { isLight, toggleTheme };
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const { isLight } = useTheme();
  useEffect(() => {
    document.documentElement.dataset.theme = isLight ? 'light' : 'dark';
    document.body.classList.toggle('light-theme', isLight);
    document.body.classList.toggle('dark-theme', !isLight);
  }, [isLight]);
  return children;
}
