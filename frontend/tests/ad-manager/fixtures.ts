import { test as base, expect, type Page } from '@playwright/test';
import type { AdvertisingOverview, DailyAdvertisingReport } from '../../src/packages/ad-manager/types';
import type { Project } from '../../src/services/auth';
import type { Customer, CustomerPage } from '../../src/services/crm';

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

export async function mockCustomerPage(page: Page, customers: Customer[] = []) {
  const counts = (values: string[]) => {
    const entries = new Map<string, number>();
    for (const value of values) entries.set(value, (entries.get(value) ?? 0) + 1);
    return Array.from(entries, ([name, count]) => ({ name, count }));
  };
  await page.route('**/api/projects/*/customers/page?**', route => {
    const params = new URL(route.request().url()).searchParams;
    const search = (params.get('search') ?? '').toLowerCase();
    const stage = params.get('stage'), city = params.get('city'), label = params.get('label');
    const filtered = customers.filter(customer =>
      (customer.name.toLowerCase().includes(search) || customer.phoneNumber.includes(search)) &&
      (!stage || stage === 'All' || (customer.pipelineStage ?? 'New') === stage) &&
      (!city || city === 'All' || customer.city === city) &&
      (!label || label === 'All' || (customer.label || 'بدون تصنيف') === label));
    const pageSize = Math.max(1, Math.min(100, Number(params.get('pageSize') || 10)));
    const pageNumber = Math.max(1, Math.min(Number(params.get('page') || 1), Math.ceil(filtered.length / pageSize) || 1));
    const response: CustomerPage = {
      items: filtered.toSorted((left, right) => left.id.localeCompare(right.id)).slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
      page: pageNumber, pageSize, totalFiltered: filtered.length, totalCustomers: customers.length,
      labels: counts(customers.map(customer => customer.label || 'بدون تصنيف')),
      stages: counts(customers.map(customer => customer.pipelineStage ?? 'New')),
      cities: [...new Set(customers.map(customer => customer.city).filter(Boolean))].sort(),
    };
    return route.fulfill({ json: response });
  });
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
