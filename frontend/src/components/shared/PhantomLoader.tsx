'use client';

import React, { useEffect } from 'react';

interface PhantomLoaderProps {
  loading: boolean;
  children: React.ReactNode;
  label?: string;
  animation?: 'shimmer' | 'pulse' | 'breathe' | 'solid';
  duration?: number;
  count?: number;
}

export default function PhantomLoader({
  loading,
  children,
  label = 'تحميل المحتوى',
  animation = 'shimmer',
  duration = 1.15,
  count = 1,
}: PhantomLoaderProps) {
  useEffect(() => {
    import('@aejkatappaja/phantom-ui');
  }, []);

  return (
    <phantom-ui
      loading={loading}
      animation={animation}
      duration={duration}
      count={count}
      shimmer-direction="rtl"
      background-color="var(--surface-muted)"
      shimmer-color="var(--accent-soft-strong)"
      fallback-radius={8}
      reveal={0.18}
      loading-label={label}
    >
      {children}
    </phantom-ui>
  );
}
