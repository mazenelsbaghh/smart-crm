import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { AdManagerShell } from '../components/AdManagerShell';
import { OverviewView } from '../components/OverviewView';
import { WhatsAppOutcomesView } from '../components/WhatsAppOutcomesView';
import type { AdvertisingOverview } from '../types';

const tabs = [{ key: 'overview', label: 'نظرة عامة' }, { key: 'settings', label: 'الإعدادات' }];

describe('AdManagerShell accessibility', () => {
  it('does not present a new Gateway conversation as a qualified lead', () => {
    const overview: AdvertisingOverview = {
      asOfUtc: '2026-08-19T05:31:03Z', windowStartUtc: '2026-08-18T21:00:00Z', windowEndUtc: '2026-08-19T05:31:03Z', spend: 0, revenue: 0, roas: 0, leads: 1, qualifiedLeads: 0,
      bookings: 0, purchases: 0, activeAds: 0, totalAds: 0, autopilot: true, emergencyStop: false,
      continuingSpend: false, dailyCap: 2000, usableCap: 1700, aiModel: 'gemini', usesProjectApiKey: true,
      reportingTimezone: 'Africa/Cairo', currency: 'EGP', attributionWindow: '7d click', truthSource: 'Gateway',
      readiness: { ready: true, items: [] }, operations: { performance: { daysLoaded: 0, snapshots: 0, impressions: 0, clicks: 0, allTimeSpend: 0 }, ai: { model: 'gemini', usesProjectApiKey: true }, tracking: { healthy: true, state: 'Healthy', mode: 'DATASET_AND_CRM', openIncidents: [] }, jobs: [] }
    };

    render(<OverviewView overview={overview} onConfigure={vi.fn()} />);

    expect(screen.getByText(/1 محادثة جديدة، 0 مؤهل/)).toBeVisible();
    expect(screen.queryByText(/1 عميل مؤهل/)).not.toBeInTheDocument();
  });

  it('does not count attribution from another outcome as attributed Gateway lead', () => {
    render(<WhatsAppOutcomesView rows={[{ id: 'lead-1', eventType: 'Lead', occurredAtUtc: '2026-08-19T05:31:03Z', state: 'Accepted', attributionMethod: 'InternalBusinessOutcome' }]}
      touches={[{ id: 'touch-1', conversionId: 'booking-1', method: 'CtwaClid', hasClickIdentifier: true, touchedAtUtc: '2026-08-19T05:00:00Z' }]}
      deliveries={[]} tracking={[]} />);

    expect(screen.getByText('منسوب لإعلان بدقة').parentElement).toHaveTextContent('٠');
    expect(screen.getByText('غير منسوب — لم نخمن')).toBeVisible();
  });

  it('keeps continuing spend and Emergency Stop visible in every view', async () => {
    const stop = vi.fn();
    render(<AdManagerShell tabs={tabs} activeTab="settings" busy={false} ready autopilot emergencyStop={false} continuingSpend onTabChange={vi.fn()} onRefresh={vi.fn()} onStop={stop} onResume={vi.fn()}><p>المحتوى</p></AdManagerShell>);
    expect(screen.getByRole('alert')).toHaveTextContent(/تظل تصرف حتى تؤكد Meta الإيقاف/);
    await userEvent.click(screen.getByRole('button', { name: /إيقاف طارئ/ }));
    expect(stop).toHaveBeenCalledOnce();
  });

  it('keeps stale content visible while announcing a background refresh', () => {
    render(<AdManagerShell tabs={tabs} activeTab="overview" busy={false} refreshing ready autopilot={false}
      emergencyStop={false} onTabChange={vi.fn()} onRefresh={vi.fn()} onStop={vi.fn()} onResume={vi.fn()}>
      <p>بيانات التقرير الحالية</p>
    </AdManagerShell>);

    expect(screen.getByText('بيانات التقرير الحالية')).toBeVisible();
    const refreshButton = screen.getByRole('button', { name: 'جارٍ تحديث بيانات مدير الإعلانات' });
    expect(refreshButton).toBeDisabled();
    expect(refreshButton).toHaveTextContent('جارٍ التحديث');
  });

  it('announces unsafe state and supports RTL arrow tab navigation', async () => {
    const select = vi.fn();
    render(<AdManagerShell tabs={tabs} activeTab="overview" busy={false} ready={false} autopilot={false} emergencyStop={false} onTabChange={select} onRefresh={vi.fn()} onStop={vi.fn()} onResume={vi.fn()}><p>المحتوى</p></AdManagerShell>);
    expect(screen.getByRole('status')).toHaveTextContent('لن يبدأ صرف');
    const overviewTab = screen.getByRole('tab', { name: 'نظرة عامة' });
    overviewTab.focus();
    await userEvent.keyboard('{ArrowLeft}');
    expect(select).toHaveBeenCalledWith('settings');
  });
});
