'use client';

import React, { Suspense } from 'react';
import { useSearchParams } from 'next/navigation';
import { useAuth } from '../../../context/auth-context';
import MessengerInbox from '../../../packages/inbox/MessengerInbox';

function MessengerInboxRoute() {
  const { activeProject } = useAuth();
  const conversationId = useSearchParams().get('conversationId') ?? '';
  return <MessengerInbox key={`${activeProject?.id ?? 'none'}:${conversationId}`} />;
}

export default function MessengerInboxPage() {
  return (
    <Suspense fallback={<div role="status" aria-live="polite">جاري تحميل محادثات ماسنجر…</div>}>
      <MessengerInboxRoute />
    </Suspense>
  );
}
