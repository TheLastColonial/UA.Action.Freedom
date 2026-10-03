import { expect, test } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke a loader records a donation and packs its items, then the donor is erased', async ({
  page,
}) => {
  // An item names its category, which only an Administrator can add, so one is made first.
  await signIn(page, 'admin');
  const adminNav = page.getByRole('navigation', { name: 'Sections' });
  await adminNav.getByRole('link', { name: 'Categories' }).click();
  await page.getByRole('link', { name: 'New category' }).click();
  const category = `Tins${String(Date.now())}`;
  await page.getByLabel('Name', { exact: true }).fill(category);
  await page.getByRole('button', { name: 'Create category' }).click();
  await expect(page.getByRole('heading', { name: `Edit ${category}` })).toBeVisible();

  // The operator login carries Dispatcher and Loader, who enter donations (O22).
  await signIn(page, 'operator');
  const nav = page.getByRole('navigation', { name: 'Sections' });
  await nav.getByRole('link', { name: 'Donors' }).click();
  await page.getByRole('link', { name: 'New donor' }).click();
  const donor = `Smoke Donor ${String(Date.now())}`;
  await page.getByLabel('Name').fill(donor);
  await page.getByRole('button', { name: 'Create donor' }).click();
  await expect(page.getByRole('heading', { name: donor })).toBeVisible();

  await page.getByRole('link', { name: 'Record a donation' }).click();
  await page.getByLabel('Notes').fill('Smoke test drop-off');
  await page.getByRole('button', { name: 'Record donation' }).click();
  await expect(page.getByText('Smoke test drop-off')).toBeVisible();

  await nav.getByRole('link', { name: 'Boxes' }).click();
  await page.getByRole('link', { name: 'New box' }).click();
  await page.getByRole('button', { name: 'Create box' }).click();
  await expect(page.getByRole('heading', { name: /Box #/ })).toBeVisible();

  await page.getByLabel('Description').fill('Tinned soup');
  await page.getByLabel('Category').selectOption({ label: category });
  await page.getByLabel('Quantity').fill('12');
  const donationOption = page.getByLabel('Donation').locator('option', { hasText: donor });
  await page
    .getByLabel('Donation')
    .selectOption({ label: (await donationOption.textContent()) ?? '' });
  await page.getByRole('button', { name: 'Add item' }).click();
  await expect(page.getByText(new RegExp(`donation #\\d+ from ${donor}`))).toBeVisible();

  // The report is the donor's account of what they gave; it names no destination.
  await nav.getByRole('link', { name: 'Donors' }).click();
  await page.getByRole('link', { name: donor }).click();
  await page.getByRole('link', { name: 'Donor report' }).click();
  await expect(page.getByRole('heading', { name: `Donation report for ${donor}` })).toBeVisible();
  await expect(page.getByText('12 items given')).toBeVisible();

  // Only an Administrator erases a donor; the donation, its items and value stay.
  await signIn(page, 'admin');
  await page
    .getByRole('navigation', { name: 'Sections' })
    .getByRole('link', { name: 'Donors' })
    .click();
  await page.getByRole('link', { name: donor }).click();
  await page.getByRole('button', { name: 'Erase donor' }).click();
  await page.getByRole('button', { name: 'Erase permanently' }).click();
  await expect(page.getByRole('heading', { name: 'Donors' })).toBeVisible();
  await expect(page.getByRole('link', { name: donor })).toHaveCount(0);
});
