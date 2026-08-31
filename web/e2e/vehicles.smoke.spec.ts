import { expect, test } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke operator creates a vehicle and finds it in the list', async ({ page }) => {
  await signIn(page, 'operator');
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();

  const nav = page.getByRole('navigation', { name: 'Sections' });
  await nav.getByRole('link', { name: 'Vehicles' }).click();
  await expect(page.getByRole('heading', { name: 'Vehicles' })).toBeVisible();

  await page.getByRole('link', { name: 'New vehicle' }).click();

  const vin = `E2E${String(Date.now())}`;
  await page.getByLabel('VIN').fill(vin);
  await page.getByLabel('Number plate').fill('E2E 001');
  await page.getByLabel('Year').fill('2015');
  await page.getByLabel('Kerb weight (kg)').fill('1800');
  await page.getByRole('button', { name: 'Create vehicle' }).click();

  await expect(page.getByRole('heading', { name: vin })).toBeVisible();

  await nav.getByRole('link', { name: 'Vehicles' }).click();
  await expect(page.getByRole('link', { name: vin })).toBeVisible();
});
