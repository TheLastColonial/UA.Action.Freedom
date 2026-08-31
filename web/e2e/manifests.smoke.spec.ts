import { expect, test } from '@playwright/test';

import { signIn } from './auth';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke a manifest is proposed against a published convoy, then approved and frozen', async ({
  page,
}) => {
  // The Administrator can do the whole path — plan, publish, propose and (uniquely) approve.
  await signIn(page, 'admin');
  const nav = page.getByRole('navigation', { name: 'Sections' });

  await nav.getByRole('link', { name: 'Convoys' }).click();
  await page.getByRole('link', { name: 'New convoy' }).click();
  await page.getByLabel('Departs').fill('2026-07-01T08:00');
  await page.getByLabel('Expected arrival').fill('2026-07-06T20:00');
  await page.getByRole('button', { name: 'Create convoy' }).click();
  await expect(page.getByRole('heading', { name: /Convoy #/ })).toBeVisible();

  const convoyId = ((await page.getByRole('heading', { name: /Convoy #/ }).textContent()) ?? '')
    .replace(/\D/g, '')
    .trim();

  await page.getByRole('button', { name: 'Publish truck list' }).click();
  await expect(page.getByText('Truck list published')).toBeVisible();

  await nav.getByRole('link', { name: 'Manifests' }).click();
  await page.getByRole('link', { name: 'New manifest' }).click();
  const reference = `E2E-${String(Date.now())}`;
  await page.getByLabel('Reference').fill(reference);
  await page.getByLabel('Convoy id').fill(convoyId);
  await page.getByRole('button', { name: 'Create manifest' }).click();
  await expect(page.getByRole('heading', { name: reference })).toBeVisible();

  await page.getByRole('button', { name: 'Status' }).click();
  await page.getByRole('button', { name: 'Propose' }).click();
  await expect(page.getByRole('heading', { name: 'Status: Proposed' })).toBeVisible();

  await page.getByRole('button', { name: 'Approve' }).click();
  await expect(page.getByText('GMR submitted — the manifest is now frozen.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Status: Confirmed' })).toBeVisible();
});
