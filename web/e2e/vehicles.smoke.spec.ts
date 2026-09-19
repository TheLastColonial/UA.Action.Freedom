import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

async function createVehicle(page: Page, vin: string): Promise<void> {
  const nav = page.getByRole('navigation', { name: 'Sections' });
  await nav.getByRole('link', { name: 'Vehicles' }).click();
  await expect(page.getByRole('heading', { name: 'Vehicles' })).toBeVisible();

  await page.getByRole('link', { name: 'New vehicle' }).click();
  await page.getByLabel('VIN').fill(vin);
  await page.getByLabel('Number plate').fill('E2E 001');
  await page.getByLabel('Year').fill('2015');
  await page.getByLabel('Kerb weight (kg)').fill('1800');
  await page.getByRole('button', { name: 'Create vehicle' }).click();

  await expect(page.getByRole('heading', { name: vin })).toBeVisible();
}

test('@smoke operator creates a vehicle and reads it back', async ({ page }) => {
  await signIn(page, 'operator');
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();

  const vin = `E2E${String(Date.now())}`;
  await createVehicle(page, vin);

  // Landing on the detail page is a read-back through GET /vehicles/{vin}.
  await expect(page.getByText('E2E 001')).toBeVisible();
  await expect(page.getByText('Not yet inspected')).toBeVisible();
});

test('@smoke a mechanic records an inspection and it survives a reload', async ({ page }) => {
  // operator carries Mechanic locally (iac/tofu/keycloak.tf).
  await signIn(page, 'operator');
  const vin = `E2E${String(Date.now())}`;
  await createVehicle(page, vin);

  await page.getByRole('link', { name: 'Servicing' }).click();
  await expect(page.getByRole('heading', { name: `Servicing — ${vin}` })).toBeVisible();
  await page.getByLabel('Inspection status').selectOption('Failed');
  await page.getByLabel(/Inspection notes/).fill('Nearside rear tyre below legal tread');
  await page.getByRole('button', { name: 'Save inspection' }).click();
  await expect(page.getByRole('status')).toHaveText('Inspection saved.');

  // A reload drops every client-side cache: what comes back is what the database holds.
  await page.reload();
  await expect(page.getByLabel('Inspection status')).toHaveValue('Failed');
  await expect(page.getByLabel(/Inspection notes/)).toHaveValue(
    'Nearside rear tyre below legal tread',
  );

  await page.getByRole('button', { name: 'Back to vehicle' }).click();
  await expect(page.getByRole('heading', { name: vin, level: 1 })).toBeVisible();
  await expect(page.getByText('Issues found', { exact: true })).toBeVisible();
  await expect(page.getByText('Nearside rear tyre below legal tread')).toBeVisible();
});
