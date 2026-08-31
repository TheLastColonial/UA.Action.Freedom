import { expect, test } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke a ground officer records a receiver and its reason-gated delivery detail', async ({
  page,
}) => {
  await signIn(page, 'groundofficer');
  const nav = page.getByRole('navigation', { name: 'Sections' });

  await nav.getByRole('link', { name: 'Receivers' }).click();
  await page.getByRole('link', { name: 'New receiver' }).click();
  const organisation = `E2E Aid ${String(Date.now())}`;
  await page.getByLabel('Organisation').fill(organisation);
  await page.getByLabel('Region').fill('Kharkiv Oblast');
  await page.getByRole('button', { name: 'Create receiver' }).click();
  await expect(page.getByRole('heading', { name: organisation })).toBeVisible();

  await page.getByRole('button', { name: 'Reveal delivery detail' }).click();
  await expect(page.getByText('This access is recorded in the receiver access log.')).toBeVisible();
  await page
    .getByLabel('Reason for viewing')
    .fill('Setting up the delivery point for this receiver');
  await page.getByRole('button', { name: 'Reveal detail' }).click();

  await page.getByRole('button', { name: 'Add delivery detail' }).click();
  await page.getByLabel('Contact name').fill('Iryna Shevchenko');
  await page.getByLabel('Contact phone').fill('+380 44 123 4567');
  await page.getByLabel('Address line 1').fill('17 Khreshchatyk');
  await page.getByLabel('City').fill('Kyiv');
  await page.getByRole('button', { name: 'Save delivery detail' }).click();

  await expect(page.getByText('17 Khreshchatyk', { exact: false })).toBeVisible();
});
