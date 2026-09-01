'use client';

import { useCallback, useEffect, useRef, useState } from 'react';

export function useUnsavedNavigationGuard(isDirty: boolean) {
  const [pendingHref, setPendingHref] = useState<string | null>(null);
  const allowNextUnloadRef = useRef(false);

  useEffect(() => {
    const warnBeforeUnload = (event: BeforeUnloadEvent) => {
      if (!isDirty || allowNextUnloadRef.current) return;
      event.preventDefault();
      event.returnValue = '';
    };

    const interceptClientNavigation = (event: MouseEvent) => {
      if (
        !isDirty
        || allowNextUnloadRef.current
        || event.defaultPrevented
        || event.button !== 0
        || event.metaKey
        || event.ctrlKey
        || event.shiftKey
        || event.altKey
        || !(event.target instanceof Element)
      ) return;

      const anchor = event.target.closest<HTMLAnchorElement>('a[href]');
      if (!anchor || anchor.target === '_blank' || anchor.hasAttribute('download')) return;

      const destination = new URL(anchor.href, window.location.href);
      if (destination.origin !== window.location.origin) return;

      const current = window.location;
      const sameDocument = destination.pathname === current.pathname && destination.search === current.search;
      if (sameDocument) return;

      event.preventDefault();
      setPendingHref(destination.href);
    };

    window.addEventListener('beforeunload', warnBeforeUnload);
    document.addEventListener('click', interceptClientNavigation, true);
    return () => {
      window.removeEventListener('beforeunload', warnBeforeUnload);
      document.removeEventListener('click', interceptClientNavigation, true);
    };
  }, [isDirty]);

  const cancelNavigation = useCallback(() => setPendingHref(null), []);
  const confirmNavigation = useCallback(() => {
    if (!pendingHref) return;
    allowNextUnloadRef.current = true;
    window.location.assign(pendingHref);
  }, [pendingHref]);

  return {
    navigationBlocked: isDirty && pendingHref !== null,
    cancelNavigation,
    confirmNavigation,
  };
}
