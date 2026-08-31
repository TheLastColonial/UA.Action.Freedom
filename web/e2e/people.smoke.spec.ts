import { expect, test } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke administrator adds a volunteer and reads them back', async ({ page }) => {
  await signIn(page, 'admin');
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();

  const nav = page.getByRole('navigation', { name: 'Sections' });
  await nav.getByRole('link', { name: 'Volunteers' }).click();
  await page.getByRole('link', { name: 'New volunteer' }).click();

  const last = `E2E${String(Date.now())}`;
  await page.getByLabel('First name').fill('Smoke');
  await page.getByLabel('Last name').fill(last);
  await page.getByLabel('Date of birth').fill('1990-01-01');
  await page.getByRole('button', { name: 'Create volunteer' }).click();

  // The detail page is a read-back through GET /people/{id}.
  await expect(page.getByRole('heading', { name: `Smoke ${last}` })).toBeVisible();
});
