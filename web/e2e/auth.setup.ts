import { mkdir, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';

import { test as setup } from '@playwright/test';

import { SEED_USERS, authFile } from './authFiles';
import { stackIsUp } from './stack';

const PASSWORD = process.env['FREEDOM_TEST_PASSWORD'] ?? 'password';
const EMPTY_STATE = JSON.stringify({ cookies: [], origins: [] });

// One real Keycloak login per seed user, saved as storage state. The SPA keeps its access
// token in memory, but the Keycloak SSO cookie in this state lets a spec silently re-auth
// on first navigation — no login form per test.
for (const user of SEED_USERS) {
  setup(`authenticate as ${user}`, async ({ page }) => {
    const path = authFile(user);
    await mkdir(dirname(path), { recursive: true });

    if (!(await stackIsUp())) {
      // Keep a file on disk so specs that reference it still load; their own guard skips them.
      await writeFile(path, EMPTY_STATE);
      setup.skip(true, 'the local stack is not up (docker compose + tofu apply)');
      return;
    }

    await page.context().clearCookies();
    await page.goto('/app/');
    await page.locator('#username').fill(user);
    await page.locator('#password').fill(PASSWORD);
    await page.locator('#kc-login').click();
    await page.waitForURL('**/app/**');

    await page.context().storageState({ path });
  });
}
