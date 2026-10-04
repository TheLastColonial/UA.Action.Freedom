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
  await page.getByLabel('Name', { exact: true }).fill('Manchester depot');
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
  await expect(page.getByRole('cell', { name, exact: true }).first()).toBeVisible();
}

test('@smoke a convoy is planned to readiness: passed vehicle, a driver, a passenger, insurance naming the drivers', async ({
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
  await page.getByLabel('Name', { exact: true }).fill('Manchester depot');
  await page.getByLabel('Postcode').fill('M1 1AA');
  await page.getByRole('button', { name: 'Save route' }).click();

  await page.getByRole('tab', { name: 'Vehicles' }).click();
  await page.getByRole('combobox', { name: 'Vehicle' }).fill(vin);
  await page.getByRole('option', { name: new RegExp(vin) }).click();
  await expect(page.getByRole('cell', { name: vin })).toBeVisible();

  await page.getByRole('tab', { name: 'Crew' }).click();
  // One driver is enough to be ready; a second is advised.
  await assignCrew(page, 'Driver', first);
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

  // Standing the passenger down leaves the policy in cover.
  await page.getByRole('tab', { name: 'Crew' }).click();
  await page.getByRole('button', { name: `Remove ${rider} from E2E 002` }).click();
  await expect(page.getByText(/Insured with Ukraine Aid Mutual/)).toBeVisible();

  // A driver added afterwards is not named on the policy until it is recorded again.
  await assignCrew(page, 'Driver', second);
  await expect(
    page.getByText(/1 driver added since the policy was recorded is not covered/),
  ).toBeVisible();

  await page.getByRole('tab', { name: 'Overview' }).click();
  await expect(page.getByRole('heading', { name: 'Not ready yet' })).toBeVisible();
  await expect(page.getByText('E2E 002: Insurance does not cover every driver')).toBeVisible();

  await page.getByRole('tab', { name: 'Crew' }).click();
  await insurance.getByRole('button', { name: 'Record insurance' }).click();
  await expect(page.getByText(/not covered/)).toBeHidden();

  await page.getByRole('tab', { name: 'Overview' }).click();
  await expect(page.getByRole('heading', { name: 'Ready to travel' })).toBeVisible();

  // Each vehicle has its own outbound ferry booking, made on the truck-list entry.
  await page.getByRole('tab', { name: 'Cargo and ferry' }).click();
  await expect(page.getByText('Ferry not booked')).toBeVisible();
  const ferry = page.getByRole('form', { name: 'Ferry for E2E 002' });
  await ferry.getByLabel('Ferry operator').fill('P&O Ferries');
  await ferry.getByLabel('Booking reference').fill(`E2E-${stamp}`);
  await ferry.getByLabel('Sailing').fill('2026-07-02T07:30');
  await ferry.getByRole('button', { name: 'Book ferry' }).click();
  await expect(page.getByText(/Booked with P&O Ferries/)).toBeVisible();
});

test('@smoke editing a route keeps its point ids, and the Dispatcher nominates a leader', async ({
  page,
}) => {
  const stamp = String(Date.now());
  const vin = `E2L${stamp}`;
  const driver = `Leader Driver${stamp}`;

  await signIn(page, 'admin');
  const nav = page.getByRole('navigation', { name: 'Sections' });
  await nav.getByRole('link', { name: 'Volunteers' }).click();
  await page.getByRole('link', { name: 'New volunteer' }).click();
  await page.getByLabel('First name').fill('Leader');
  await page.getByLabel('Last name').fill(`Driver${stamp}`);
  await page.getByLabel('Date of birth').fill('1985-01-01');
  await page.getByLabel('Volunteers to drive').check();
  await page.getByRole('button', { name: 'Create volunteer' }).click();
  await expect(page.getByRole('heading', { name: driver })).toBeVisible();

  await signIn(page, 'operator');
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
  await page.getByLabel('Departs').fill('2026-08-01T08:00');
  await page.getByLabel('Expected arrival').fill('2026-08-06T20:00');
  await page.getByRole('button', { name: 'Create convoy' }).click();
  await expect(page.getByRole('heading', { name: /Convoy #/ })).toBeVisible();

  // Two points, then swap them: the ids go with the points, not the positions.
  await page.getByRole('tab', { name: 'Route' }).click();
  for (const [index, name] of ['Coventry depot', 'Dover port'].entries()) {
    await page.getByRole('button', { name: 'Add stop' }).click();
    await page.getByLabel('Name', { exact: true }).nth(index).fill(name);
    await page.getByLabel('Postcode').nth(index).fill('M1 1AA');
  }
  const saved = page.waitForResponse(
    (r) => r.url().includes('/route') && r.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Save route' }).click();
  const before = (await (await saved).json()) as { routePointId: number; name: string }[];

  await page.getByRole('button', { name: 'Move up' }).nth(1).click();
  const resaved = page.waitForResponse(
    (r) => r.url().includes('/route') && r.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Save route' }).click();
  const after = (await (await resaved).json()) as { routePointId: number; name: string }[];

  expect(after.map((p) => p.name)).toEqual(['Dover port', 'Coventry depot']);
  expect(after.map((p) => p.routePointId)).toEqual(before.map((p) => p.routePointId).reverse());

  await page.getByRole('tab', { name: 'Vehicles' }).click();
  await page.getByRole('combobox', { name: 'Vehicle' }).fill(vin);
  await page.getByRole('option', { name: new RegExp(vin) }).click();
  await expect(page.getByRole('cell', { name: vin })).toBeVisible();

  await page.getByRole('tab', { name: 'Crew' }).click();
  await page.getByLabel('Add driver to E2E 003').selectOption({ label: driver });
  await page.getByRole('button', { name: 'Assign' }).click();
  await expect(page.getByRole('cell', { name: driver, exact: true }).first()).toBeVisible();

  await page.getByLabel('Nominate leader').selectOption({ label: driver });
  await page.getByRole('button', { name: 'Nominate' }).click();
  await expect(page.getByText(`${driver} leads this convoy.`)).toBeVisible();

  // Flag the first stop as overnight: the crew member is not covered until somebody books it or arranges it.
  await page.getByRole('tab', { name: 'Route' }).click();
  await page.getByLabel('Kind').first().selectOption('Overnight');
  const flagged = page.waitForResponse(
    (r) => r.url().includes('/route') && r.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: 'Save route' }).click();
  await flagged;

  await page.getByRole('tab', { name: 'Accommodation' }).click();
  await expect(page.getByRole('cell', { name: `${driver} at Dover port: Missing` })).toBeVisible();

  await page.getByLabel('Provider').fill('Premier Inn Dover');
  await page.getByLabel('Check in').fill('2026-08-01');
  await page.getByLabel('Check out').fill('2026-08-02');
  await page.getByRole('checkbox', { name: driver }).check();
  await page.getByRole('button', { name: 'Book accommodation' }).click();
  await expect(page.getByRole('cell', { name: `${driver} at Dover port: Booked` })).toBeVisible();
});

test('@smoke operator allocates a convoy budget, enters fuel over the line and sees it flagged', async ({
  page,
}) => {
  await signIn(page, 'operator');
  const nav = page.getByRole('navigation', { name: 'Sections' });

  await nav.getByRole('link', { name: 'Convoys' }).click();
  await page.getByRole('link', { name: 'New convoy' }).click();
  await page.getByLabel('Departs').fill('2026-08-01T08:00');
  await page.getByLabel('Expected arrival').fill('2026-08-06T20:00');
  await page.getByRole('button', { name: 'Create convoy' }).click();
  await expect(page.getByRole('heading', { name: /Convoy #/ })).toBeVisible();

  // No budget is advice, never a blocker.
  await expect(page.getByText('No budget set')).toBeVisible();

  await page.getByRole('tab', { name: 'Budget' }).click();
  await page.getByLabel('Fuel budget (£)').fill('1000');
  await page.getByRole('button', { name: 'Save budget' }).click();
  await expect(page.getByRole('cell', { name: '£1000.00' })).toBeVisible();

  await page.getByLabel('Amount (£)').fill('1100');
  await page.getByRole('button', { name: 'Add cost' }).click();
  await expect(page.getByRole('cell', { name: 'Over budget' })).toBeVisible();

  await page.getByRole('tab', { name: 'Overview' }).click();
  await expect(page.getByText('Fuel is over budget')).toBeVisible();
});
