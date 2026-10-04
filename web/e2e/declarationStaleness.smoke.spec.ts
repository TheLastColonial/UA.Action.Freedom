import { expect, test } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke taking a box off a vehicle makes its filed GMR stale, and withdrawing it clears the task', async ({
  page,
}) => {
  const stamp = String(Date.now());
  const vin = `E2ES${stamp}`;
  await signIn(page, 'admin');
  const nav = page.getByRole('navigation', { name: 'Sections' });

  await nav.getByRole('link', { name: 'Boxes' }).click();
  await page.getByRole('link', { name: 'New box' }).click();
  await page.getByRole('button', { name: 'Create box' }).click();
  await expect(page.getByRole('heading', { name: /Box #/ })).toBeVisible();
  const boxId = /Box #(\d+)/.exec(
    await page.getByRole('heading', { name: /Box #/ }).innerText(),
  )?.[1];
  expect(boxId).toBeDefined();

  await nav.getByRole('link', { name: 'Vehicles' }).click();
  await page.getByRole('link', { name: 'New vehicle' }).click();
  await page.getByLabel('VIN').fill(vin);
  await page.getByLabel('Number plate').fill('E2E 009');
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
  const convoyUrl = page.url();

  await page.getByRole('tab', { name: 'Vehicles' }).click();
  await page.getByRole('combobox', { name: 'Vehicle' }).fill(vin);
  await page.getByRole('option', { name: new RegExp(vin) }).click();
  await expect(page.getByRole('cell', { name: vin })).toBeVisible();
  await page.getByRole('tab', { name: 'Overview' }).click();
  await page.getByRole('button', { name: 'Publish truck list' }).click();
  await expect(page.getByText('Truck list published')).toBeVisible();

  // The box is the vehicle's cargo.
  await page.getByRole('tab', { name: 'Cargo and ferry' }).click();
  await page.getByLabel(/Box id to add to/).fill(boxId ?? '');
  await page.getByRole('button', { name: 'Add box' }).click();
  await expect(page.getByRole('cell', { name: `#${boxId ?? ''}` })).toBeVisible();

  // The GMR is recorded against that load, from the vehicle's manifest.
  await page.getByRole('tab', { name: 'Vehicles' }).click();
  await page.getByRole('link', { name: 'Open manifest' }).click();
  await page.getByLabel('Reference').fill(`E2E-${stamp}`);
  await page.getByRole('button', { name: 'Open manifest' }).click();
  await expect(page.getByRole('heading', { name: `E2E-${stamp}` })).toBeVisible();
  const manifestUrl = page.url();
  await page.getByRole('tab', { name: 'Declarations' }).click();
  await page.getByLabel('GMR reference').fill(`GMR-${stamp}`);
  await page.getByRole('button', { name: 'Record GMR reference' }).click();
  await expect(page.getByText(`GMR: Filed — reference GMR-${stamp}.`)).toBeVisible();

  // Changing the load: nobody flags anything, the declaration just reads Stale.
  await page.goto(convoyUrl);
  await page.getByRole('tab', { name: 'Cargo and ferry' }).click();
  await page.getByRole('button', { name: 'Remove' }).click();
  await expect(page.getByText('No boxes on this vehicle yet.')).toBeVisible();

  await page.getByRole('tab', { name: 'Overview' }).click();
  await expect(page.getByText(/GMR for .* is stale\./)).toBeVisible();

  await page.goto(manifestUrl);
  await page.getByRole('tab', { name: 'Declarations' }).click();
  await expect(page.getByText(/the load has changed since GMR was filed/)).toBeVisible();
  await page.getByRole('button', { name: 'Withdraw GMR and re-declare' }).click();
  await expect(page.getByText('GMR: Draft.')).toBeVisible();

  await page.goto(convoyUrl);
  await expect(page.getByText('Every declaration matches its load.')).toBeVisible();
});
