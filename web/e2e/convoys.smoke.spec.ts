import { expect, test } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke operator plans a convoy, adds a route stop and publishes the truck list', async ({
  page,
}) => {
  await signIn(page, 'operator');
  const nav = page.getByRole('navigation', { name: 'Sections' });

  await nav.getByRole('link', { name: 'Convoys' }).click();
  await page.getByRole('link', { name: 'New convoy' }).click();
  await page.getByLabel('Departs').fill('2026-06-01T08:00');
  await page.getByLabel('Expected arrival').fill('2026-06-06T20:00');
  await page.getByRole('button', { name: 'Create convoy' }).click();

  await expect(page.getByRole('heading', { name: /Convoy #/ })).toBeVisible();

  await page.getByRole('tab', { name: 'Route' }).click();
  await page.getByRole('button', { name: 'Add stop' }).click();
  await page.getByLabel('Postcode').fill('M1 1AA');
  await page.getByRole('button', { name: 'Save route' }).click();

  await page.getByRole('tab', { name: 'Overview' }).click();
  await page.getByRole('button', { name: 'Publish truck list' }).click();
  await expect(page.getByText('Truck list published')).toBeVisible();
});

test('@smoke a passed vehicle joins a convoy and a dispatcher crews it', async ({ page }) => {
  const stamp = String(Date.now());
  const vin = `E2E${stamp}`;
  const surname = `Driver${stamp}`;

  // Only the Administrator adds volunteers.
  await signIn(page, 'admin');
  const adminNav = page.getByRole('navigation', { name: 'Sections' });
  await adminNav.getByRole('link', { name: 'Volunteers' }).click();
  await page.getByRole('link', { name: 'New volunteer' }).click();
  await page.getByLabel('First name').fill('Olena');
  await page.getByLabel('Last name').fill(surname);
  await page.getByLabel('Date of birth').fill('1985-01-01');
  await page.getByLabel('Volunteers to drive').check();
  await page.getByRole('button', { name: 'Create volunteer' }).click();
  await expect(page.getByRole('heading', { name: `Olena ${surname}` })).toBeVisible();

  // operator is Purchaser + Mechanic + Dispatcher: add the vehicle, pass it, plan the convoy.
  await signIn(page, 'operator');
  const nav = page.getByRole('navigation', { name: 'Sections' });
  await nav.getByRole('link', { name: 'Vehicles' }).click();
  await page.getByRole('link', { name: 'New vehicle' }).click();
  await page.getByLabel('VIN').fill(vin);
  await page.getByLabel('Number plate').fill('E2E 002');
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

  await page.getByRole('tab', { name: 'Crew' }).click();
  await page.getByLabel('Add driver to E2E 002').selectOption({ label: `Olena ${surname}` });
  await page.getByRole('button', { name: 'Assign' }).click();
  await expect(page.getByRole('cell', { name: `Olena ${surname}`, exact: true })).toBeVisible();
});
