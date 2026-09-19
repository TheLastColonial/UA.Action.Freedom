import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';

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

async function addVolunteer(page: Page, first: string, last: string, drives: boolean) {
  const nav = page.getByRole('navigation', { name: 'Sections' });
  await nav.getByRole('link', { name: 'Volunteers' }).click();
  await page.getByRole('link', { name: 'New volunteer' }).click();
  await page.getByLabel('First name').fill(first);
  await page.getByLabel('Last name').fill(last);
  await page.getByLabel('Date of birth').fill('1985-01-01');
  if (drives) {
    await page.getByLabel('Volunteers to drive').check();
  }
  await page.getByRole('button', { name: 'Create volunteer' }).click();
  await expect(page.getByRole('heading', { name: `${first} ${last}` })).toBeVisible();
}

async function assignCrew(page: Page, role: 'Driver' | 'Passenger', name: string) {
  await page.getByLabel('Role on E2E 002').selectOption(role);
  await page.getByLabel(`Add ${role.toLowerCase()} to E2E 002`).selectOption({ label: name });
  await page.getByRole('button', { name: 'Assign' }).click();
  await expect(page.getByRole('cell', { name, exact: true })).toBeVisible();
}

test('@smoke a convoy is planned to readiness: passed vehicle, two drivers, a passenger, insurance', async ({
  page,
}) => {
  const stamp = String(Date.now());
  const vin = `E2E${stamp}`;
  const first = `Olena Driver${stamp}`;
  const second = `Taras Driver${stamp}`;
  const rider = `Mykola Rider${stamp}`;

  // Only the Administrator adds volunteers.
  await signIn(page, 'admin');
  await addVolunteer(page, 'Olena', `Driver${stamp}`, true);
  await addVolunteer(page, 'Taras', `Driver${stamp}`, true);
  await addVolunteer(page, 'Mykola', `Rider${stamp}`, false);

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

  await page.getByRole('tab', { name: 'Route' }).click();
  await page.getByRole('button', { name: 'Add stop' }).click();
  await page.getByLabel('Postcode').fill('M1 1AA');
  await page.getByRole('button', { name: 'Save route' }).click();

  await page.getByRole('tab', { name: 'Vehicles' }).click();
  await page.getByRole('combobox', { name: 'Vehicle' }).fill(vin);
  await page.getByRole('option', { name: new RegExp(vin) }).click();
  await expect(page.getByRole('cell', { name: vin })).toBeVisible();

  await page.getByRole('tab', { name: 'Crew' }).click();
  await assignCrew(page, 'Driver', first);
  await assignCrew(page, 'Driver', second);
  await assignCrew(page, 'Passenger', rider);

  const insurance = page.getByRole('form', { name: 'Insurance for E2E 002' });
  await insurance.getByLabel('Insurer').fill('Ukraine Aid Mutual');
  await insurance.getByLabel('Policy number').fill(`E2E-${stamp}`);
  await insurance.getByLabel('Cover starts').fill('2026-06-30');
  await insurance.getByLabel('Cover ends').fill('2026-07-10');
  await insurance.getByRole('button', { name: 'Record insurance' }).click();
  await expect(page.getByText(/Insured with Ukraine Aid Mutual/)).toBeVisible();

  await page.getByRole('tab', { name: 'Overview' }).click();
  await expect(page.getByRole('heading', { name: 'Ready to travel' })).toBeVisible();

  // The policy names the crew: standing the passenger down voids it, and readiness notices.
  await page.getByRole('tab', { name: 'Crew' }).click();
  await page.getByRole('button', { name: `Remove ${rider}` }).click();
  await expect(page.getByText(/voided by a crew change/)).toBeVisible();

  await page.getByRole('tab', { name: 'Overview' }).click();
  await expect(page.getByRole('heading', { name: 'Not ready yet' })).toBeVisible();
  await expect(page.getByText('E2E 002: Insurance voided by a crew change')).toBeVisible();
});
