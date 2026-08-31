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
  await page.getByLabel('City').fill('Dnipro');
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
