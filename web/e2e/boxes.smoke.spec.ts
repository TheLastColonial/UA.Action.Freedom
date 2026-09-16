import { expect, test } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke loader packs a box, adds an item and validates it', async ({ page }) => {
  // The operator login carries Loader (pack + validate) and Purchaser (needed to add the
  // volunteer this test validates against).
  await signIn(page, 'admin');
  let nav = page.getByRole('navigation', { name: 'Sections' });

  // A volunteer to validate against.
  await nav.getByRole('link', { name: 'Volunteers' }).click();
  await page.getByRole('link', { name: 'New volunteer' }).click();
  const checker = `Checker${String(Date.now())}`;
  await page.getByLabel('First name').fill('Box');
  await page.getByLabel('Last name').fill(checker);
  await page.getByLabel('Date of birth').fill('1988-02-02');
  await page.getByRole('button', { name: 'Create volunteer' }).click();
  await expect(page.getByRole('heading', { name: `Box ${checker}` })).toBeVisible();

  await signIn(page, 'operator');
  nav = page.getByRole('navigation', { name: 'Sections' });
  await nav.getByRole('link', { name: 'Boxes' }).click();
  await page.getByRole('link', { name: 'New box' }).click();
  await page.getByRole('button', { name: 'Create box' }).click();
  await expect(page.getByRole('heading', { name: /Box #/ })).toBeVisible();

  await page.getByLabel('Description').fill('Sleeping bags');
  await page.getByRole('button', { name: 'Add item' }).click();
  await expect(page.getByText('Sleeping bags')).toBeVisible();

  await page.getByLabel('Checked by').selectOption({ label: `Box ${checker}` });
  await page.getByLabel('Confirmed weight (kg)').fill('14');
  await page.getByRole('button', { name: 'Validate box' }).click();

  await expect(page.getByText('Validated', { exact: true })).toBeVisible();
  await expect(
    page.getByText('This box has been validated — its contents are now fixed.'),
  ).toBeVisible();
});

test('@smoke a loader issues a QR label for a box and it is ready to print', async ({ page }) => {
  await signIn(page, 'operator');
  const nav = page.getByRole('navigation', { name: 'Sections' });

  await nav.getByRole('link', { name: 'Boxes' }).click();
  await page.getByRole('link', { name: 'New box' }).click();
  await page.getByRole('button', { name: 'Create box' }).click();
  await expect(page.getByRole('heading', { name: /Box #/ })).toBeVisible();

  await expect(page.getByRole('heading', { name: 'QR label' })).toBeVisible();
  await expect(page.getByText('This box has no QR label.')).toBeVisible();

  await page.getByRole('button', { name: 'Issue label' }).click();

  await expect(page.getByText('Label issued', { exact: false })).toBeVisible();
  await expect(page.getByRole('img', { name: /QR label for box \d+/ })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Print label' })).toBeEnabled();
});

test('@smoke a loader places a box in a bay and then finds it there', async ({ page }) => {
  // A location and a bay for the box to be placed in — Administrator-only, so this is a
  // separate signed-in session from the rest of the test.
  await signIn(page, 'admin');
  const adminNav = page.getByRole('navigation', { name: 'Sections' });
  await adminNav.getByRole('link', { name: 'Locations' }).click();
  await page.getByRole('link', { name: 'New location' }).click();
  const depot = `Depot${String(Date.now())}`;
  await page.getByLabel('Name').fill(depot);
  await page.getByRole('button', { name: 'Create location' }).click();
  await expect(page.getByRole('heading', { name: depot })).toBeVisible();

  await page.getByLabel('Bay code').fill('A1');
  await page.getByRole('button', { name: 'Add bay' }).click();
  await expect(page.getByText('A1')).toBeVisible();

  // A volunteer to name as having placed the box — its own name so the select is unambiguous
  // even against the accumulated volunteers from earlier runs.
  await page
    .getByRole('navigation', { name: 'Sections' })
    .getByRole('link', { name: 'Volunteers' })
    .click();
  await page.getByRole('link', { name: 'New volunteer' }).click();
  const loaderName = `Bay${String(Date.now())}`;
  await page.getByLabel('First name').fill('Placed');
  await page.getByLabel('Last name').fill(loaderName);
  await page.getByLabel('Date of birth').fill('1991-03-03');
  await page.getByRole('button', { name: 'Create volunteer' }).click();
  await expect(page.getByRole('heading', { name: `Placed ${loaderName}` })).toBeVisible();

  // The operator login carries Loader — checks the box in at the depot, then places it in
  // the bay, all within one session (a fresh sign-in always lands on the dashboard, so the
  // box has to be created here rather than reached by a deep link from the admin session).
  await signIn(page, 'operator');
  const nav = page.getByRole('navigation', { name: 'Sections' });
  await nav.getByRole('link', { name: 'Boxes' }).click();
  await page.getByRole('link', { name: 'New box' }).click();
  await page.getByLabel('Distribution hub').selectOption({ label: depot });
  await page.getByRole('button', { name: 'Create box' }).click();
  await expect(page.getByRole('heading', { name: /Box #/ })).toBeVisible();

  await page.getByLabel('Bay').selectOption({ label: 'A1' });
  await page.getByLabel('Placed by').selectOption({ label: `Placed ${loaderName}` });
  await page.getByRole('button', { name: 'Place in bay' }).click();

  await expect(page.getByText(/Currently in bay.*A1/)).toBeVisible();

  await page.getByRole('button', { name: 'Vacate bay' }).click();
  await expect(page.getByText('Not currently in a bay.')).toBeVisible();
});
