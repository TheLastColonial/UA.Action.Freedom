import type { Page } from '@playwright/test';

const PASSWORD = process.env['FREEDOM_TEST_PASSWORD'] ?? 'password';

/**
 * Drives the real Keycloak login form — this exercises the Authorization Code + PKCE round
 * trip end to end. Seed logins: `admin`, `operator`, `groundofficer`.
 */
export async function signIn(page: Page, username: string): Promise<void> {
  await page.goto('/app/');
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(PASSWORD);
  await page.locator('#kc-login').click();
  await page.waitForURL('**/app/**');
}
