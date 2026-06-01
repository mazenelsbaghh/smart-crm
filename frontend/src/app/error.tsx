'use client';

import React from 'react';
import ErrorBoundary from '../packages/error/ErrorBoundary';

interface ErrorProps {
  error: Error & { digest?: string };
  reset: () => void;
}

export default function Error({ error, reset }: ErrorProps) {
  return <ErrorBoundary error={error} reset={reset} />;
}
