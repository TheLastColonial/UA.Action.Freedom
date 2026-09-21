import { expect, test } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke a manifest is opened on a truck list, proposed, then approved and frozen', async ({
  page,
}) => {
  // The Administrator can do the whole path — plan, publish, propose and (uniquely) approve.
  const stamp = String(Date.now());
  const vin = `E2EM${stamp}`;
  await signIn(page, 'admin');
  const nav = page.getByRole('navigation', { name: 'Sections' });

  // A manifest is the paperwork for one vehicle on one convoy, so the convoy needs a vehicle on
  // its truck list before there is anything to open a manifest against.
  await nav.getByRole('link', { name: 'Vehicles' }).click();
  await page.getByRole('link', { name: 'New vehicle' }).click();
  await page.getByLabel('VIN').fill(vin);
  await page.getByLabel('Number plate').fill('E2E 003');
  await page.getByLabel('Year').fill('2016');
  await page.getByLabel('Kerb weight (kg)').fill('2100');
  await page.getByRole('button', { name: 'Create vehicle' }).click();
  await page.getByRole('link', { name: 'Servicing' }).click();
  await page.getByLabel('Inspection status').selectOption('Passed');
  await page.getByRole('button', { name: 'Save inspection' }).click();
  await expect(page.getByRole('status')).toHaveText('Inspection saved.');

  await nav.getByRole('link', { name: 'Convoys' }).click();
  await page.getByRole('link', { name: 'New convoy' }).click();
  await page.getByLabel('Departs').fill('2026-07-01T08:00');
  await page.getByLabel('Expected arrival').fill('2026-07-06T20:00');
  await page.getByRole('button', { name: 'Create convoy' }).click();
  await expect(page.getByRole('heading', { name: /Convoy #/ })).toBeVisible();

  await page.getByRole('tab', { name: 'Vehicles' }).click();
  await page.getByRole('combobox', { name: 'Vehicle' }).fill(vin);
  await page.getByRole('option', { name: new RegExp(vin) }).click();
  await expect(page.getByRole('cell', { name: vin })).toBeVisible();

  // Publishing lives on the Overview tab, and each tab panel is only in the DOM while it is the
  // active one — so the tab has to be switched back, not merely scrolled to.
  await page.getByRole('tab', { name: 'Overview' }).click();
  await page.getByRole('button', { name: 'Publish truck list' }).click();
  await expect(page.getByText('Truck list published')).toBeVisible();

  // Opened from the truck-list entry: there is no form that names a convoy and a VIN.
  await page.getByRole('tab', { name: 'Vehicles' }).click();
  await page.getByRole('link', { name: 'Open manifest' }).click();
  const reference = `E2E-${stamp}`;
  await page.getByLabel('Reference').fill(reference);
  await page.getByRole('button', { name: 'Open manifest' }).click();
  await expect(page.getByRole('heading', { name: reference })).toBeVisible();

  await page.getByRole('tab', { name: 'Status' }).click();
  await page.getByRole('button', { name: 'Propose' }).click();
  await expect(page.getByRole('heading', { name: 'Status: Proposed' })).toBeVisible();

  await page.getByRole('button', { name: 'Approve' }).click();
  await expect(page.getByText('GMR submitted — the manifest is now frozen.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Status: Confirmed' })).toBeVisible();
});
