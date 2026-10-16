'use client';

import { useEffect, type RefObject } from 'react';

export function useDialogViewport(containerRef: RefObject<HTMLElement | null>, open: boolean) {
  useEffect(() => {
    if (!open) return;
    const container = containerRef.current;
    const viewport = window.visualViewport;
    if (!container || !viewport) return;

    const sizeToViewport = () => {
      // Pinch zoom should magnify the dialog rather than reposition its controls.
      if (viewport.scale !== 1) return;
      container.style.setProperty('--dialog-viewport-height', `${viewport.height}px`);
      container.style.setProperty('--dialog-viewport-top', `${viewport.offsetTop}px`);
    };
    sizeToViewport();
    viewport.addEventListener('resize', sizeToViewport);
    viewport.addEventListener('scroll', sizeToViewport);
    return () => {
      viewport.removeEventListener('resize', sizeToViewport);
      viewport.removeEventListener('scroll', sizeToViewport);
    };
  }, [containerRef, open]);
}
