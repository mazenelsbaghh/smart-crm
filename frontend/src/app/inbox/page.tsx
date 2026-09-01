'use client';

import React, { Suspense } from 'react';
import { useSearchParams } from 'next/navigation';
import { useAuth } from '../../context/auth-context';
import Inbox from '../../packages/inbox/Inbox';

function InboxRoute() {
  const { activeProject } = useAuth();
  const searchParams = useSearchParams();
  const routeKey = `${activeProject?.id ?? 'none'}:${searchParams.get('conversationId') ?? ''}:${searchParams.get('customerId') ?? ''}`;
  return <Inbox key={routeKey} />;
}

export default function InboxPage() {
  return (
    <Suspense fallback={<div role="status" aria-label="جاري فتح صندوق المحادثات" />}>
      <InboxRoute />
    </Suspense>
  );
}
