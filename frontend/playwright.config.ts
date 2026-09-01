import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests/ad-manager',
  timeout: 30_000,
  expect: { timeout: 8_000 },
  use: { baseURL: 'http://localhost:3000', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  webServer: { command: 'npm run dev -- --hostname localhost', url: 'http://localhost:3000', reuseExistingServer: true, timeout: 120_000 },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
