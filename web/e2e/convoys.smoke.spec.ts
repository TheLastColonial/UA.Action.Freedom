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

  await page.getByRole('button', { name: 'Route' }).click();
  await page.getByRole('button', { name: 'Add stop' }).click();
  await page.getByLabel('Postcode').fill('M1 1AA');
  await page.getByRole('button', { name: 'Save route' }).click();

  await page.getByRole('button', { name: 'Overview' }).click();
  await page.getByRole('button', { name: 'Publish truck list' }).click();
  await expect(page.getByText('Truck list published')).toBeVisible();
});
