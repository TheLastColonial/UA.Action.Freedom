import { expect, test } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke a receiver is pending until an Administrator registers it, and then a box can take it', async ({
  page,
}) => {
  const organisation = `E2E Registered ${String(Date.now())}`;

  // The Ground Officer records the receiver, and cannot register it.
  await signIn(page, 'groundofficer');
  await page
    .getByRole('navigation', { name: 'Sections' })
    .getByRole('link', { name: 'Receivers' })
    .click();
  await page.getByRole('link', { name: 'New receiver' }).click();
  await page.getByLabel('Organisation').fill(organisation);
  await page.getByLabel('Region').fill('Kharkiv Oblast');
  await page.getByRole('button', { name: 'Create receiver' }).click();
  await expect(page.getByRole('heading', { name: organisation })).toBeVisible();
  await expect(page.getByText('Pending registration')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save status' })).toHaveCount(0);

  // The Administrator registers it.
  await signIn(page, 'admin');
  await page
    .getByRole('navigation', { name: 'Sections' })
    .getByRole('link', { name: 'Receivers' })
    .click();
  await page.getByRole('link', { name: organisation }).click();
  await page.getByLabel('Registration status').selectOption('Registered');
  await page.getByRole('button', { name: 'Save status' }).click();
  await expect(page.locator('[data-status="Registered"]')).toBeVisible();

  // Now a Loader can address a box to it.
  await signIn(page, 'operator');
  await page
    .getByRole('navigation', { name: 'Sections' })
    .getByRole('link', { name: 'Boxes' })
    .click();
  await page.getByRole('link', { name: 'New box' }).click();
  await page.getByLabel('Receiver').selectOption({ label: `${organisation} — Kharkiv Oblast` });
  await page.getByRole('button', { name: 'Create box' }).click();
  await expect(page.getByRole('heading', { name: /Box #/ })).toBeVisible();
});
