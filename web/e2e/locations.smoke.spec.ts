import { expect, test } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke administrator creates a location and adds a bay to it', async ({ page }) => {
  await signIn(page, 'admin');
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();

  const nav = page.getByRole('navigation', { name: 'Sections' });
  await nav.getByRole('link', { name: 'Locations' }).click();
  await page.getByRole('link', { name: 'New location' }).click();

  const name = `E2E Depot ${String(Date.now())}`;
  await page.getByLabel('Name').fill(name);
  await page.getByLabel('City').fill('Coventry');
  await page.getByRole('button', { name: 'Create location' }).click();

  await expect(page.getByRole('heading', { name })).toBeVisible();
  await expect(page.getByText('No bays at this location yet.')).toBeVisible();

  await page.getByLabel('Bay code').fill('A1');
  await page.getByRole('button', { name: 'Add bay' }).click();

  await expect(page.getByText('A1')).toBeVisible();
});

test('@smoke a loader may read locations but not create one', async ({ page }) => {
  await signIn(page, 'operator');
  const nav = page.getByRole('navigation', { name: 'Sections' });
  await nav.getByRole('link', { name: 'Locations' }).click();

  await expect(page.getByRole('heading', { name: 'Distribution hubs' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'New location' })).toHaveCount(0);
});
