import { test as base, expect, type Page } from '@playwright/test';
import type { AdvertisingOverview, DailyAdvertisingReport } from '../../src/packages/ad-manager/types';
import type { Project } from '../../src/services/auth';

const project: Project = { id: '11111111-1111-1111-1111-111111111111', name: 'مشروع الاختبار', settings: { aiAutoReplyEnabled: true } };
const asOfUtc = new Date().toISOString();
const defaultOverview: AdvertisingOverview = {
  asOfUtc,
  windowStartUtc: new Date(Date.now() - 24 * 60 * 60 * 1_000).toISOString(),
  windowEndUtc: asOfUtc,
  spend: 0, revenue: 0, roas: 0, leads: 0, qualifiedLeads: 0, bookings: 0, purchases: 0,
  activeAds: 0, totalAds: 0, autopilot: false, emergencyStop: false, continuingSpend: false, dailyCap: 100,
  usableCap: 85, aiModel: 'gemini', usesProjectApiKey: false, reportingTimezone: 'Africa/Cairo', currency: 'EGP',
  attributionWindow: '7d click', truthSource: 'CRM + WhatsApp', readiness: { ready: false, items: [{ key: 'tracking', label: 'تتبع WhatsApp', ready: false, reason: 'WAIT: Dataset/referral غير مثبت' }] },
  operations: { connection: null, campaign: null, performance: { daysLoaded: 0, snapshots: 0, impressions: 0, clicks: 0, allTimeSpend: 0 }, ai: { model: 'gemini', usesProjectApiKey: false, latestDecision: null }, tracking: { healthy: false, state: 'Unsafe', mode: 'UNSAFE_NO_DATASET', openIncidents: [] }, jobs: [], lastFailure: null }
};
const dailyReport: DailyAdvertisingReport = {
  date: asOfUtc.slice(0, 10),
  timezone: 'Africa/Cairo',
  currency: 'EGP',
  startUtc: defaultOverview.windowStartUtc,
  endUtc: defaultOverview.windowEndUtc,
  totals: { entrants: 0, qualified: 0, bookings: 0, spend: 0 },
  rows: [],
  unattributed: { entrants: 0, qualified: 0, bookings: 0 },
};

export async function mockAdManager(page: Page, overrides: Record<string, unknown> = {}) {
  const state = { ...defaultOverview, ...overrides };
  await page.route('**/api/**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/projects') return route.fulfill({ json: [{ id: project.id, name: project.name }] });
    if (path === `/api/projects/${project.id}`) return route.fulfill({ json: project });
    if (path.endsWith('/emergency-stop') && request.method() === 'POST') { state.emergencyStop = true; return route.fulfill({ json: { state: 'Active' } }); }
    if (path.endsWith('/emergency-stop/resume') && request.method() === 'POST') { state.emergencyStop = false; return route.fulfill({ json: { state: 'Recovered' } }); }
    if (path.endsWith('/overview')) return route.fulfill({ json: state });
    if (path.endsWith('/stop-state')) return route.fulfill({ json: { emergencyStop: state.emergencyStop ? { id: 'stop-1', trigger: 'Manual', state: 'Paused', reason: 'Operator', activatedAtUtc: new Date().toISOString(), progress: { total: 3, succeeded: 3, unknown: 0, failed: 0, pending: 0, continuingSpend: false } } : null, disable: null } });
    if (path.endsWith('/daily-reports')) return route.fulfill({ json: dailyReport });
    if (path.endsWith('/strategy')) return route.fulfill({ json: { state: 'WAIT', blockingReasons: ['ADS_TRACKING_UNSAFE'], rankedOffers: [] } });
    if (path.endsWith('/connection')) return route.fulfill({ json: null });
    if (path.includes('/operations/')) return route.fulfill({ json: { state: 'Completed' } });
    return route.fulfill({ json: [] });
  });
}

export async function mockAuthorizedProject(page: Page) {
  await page.route('**/api/projects', route => route.fulfill({ json: [{ id: project.id, name: project.name }] }));
  await page.route(`**/api/projects/${project.id}`, route => route.fulfill({ json: project }));
}

export const test = base.extend({
  page: async ({ page }, provide) => {
    await page.addInitScript(({ activeProject }) => {
      localStorage.setItem('accessToken', 'test-token');
      localStorage.setItem('user', JSON.stringify({ id: 'owner', email: 'owner@example.test', fullName: 'Owner', role: 'Owner' }));
      localStorage.setItem('activeProject', JSON.stringify(activeProject));
    }, { activeProject: project });
    await mockAdManager(page);
    await provide(page);
  }
});

export { expect };
