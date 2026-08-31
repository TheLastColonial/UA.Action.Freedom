import { expect, test } from '@playwright/test';

import { authFile } from './authFiles';
import { stackIsUp } from './stack';

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

const OPERATIONAL = ['Vehicles', 'Volunteers', 'Convoys', 'Boxes', 'Manifests', 'Receivers'];

test.describe('operator (Dispatcher + Loader + Purchaser)', () => {
  test.use({ storageState: authFile('operator') });

  test('@smoke sees every operational section', async ({ page }) => {
    await page.goto('/app/');
    const nav = page.getByRole('navigation', { name: 'Sections' });
    await expect(nav.getByRole('link', { name: 'Dashboard' })).toBeVisible();
    for (const section of OPERATIONAL) {
      await expect(nav.getByRole('link', { name: section })).toBeVisible();
    }
  });
});

test.describe('administrator', () => {
  test.use({ storageState: authFile('admin') });

  test('@smoke sees every section and can add a volunteer', async ({ page }) => {
    await page.goto('/app/');
    const nav = page.getByRole('navigation', { name: 'Sections' });
    for (const section of OPERATIONAL) {
      await expect(nav.getByRole('link', { name: section })).toBeVisible();
    }

    await nav.getByRole('link', { name: 'Volunteers' }).click();
    await expect(page.getByRole('link', { name: 'New volunteer' })).toBeVisible();
  });
});

test.describe('ground officer', () => {
  test.use({ storageState: authFile('groundofficer') });

  test('@smoke sees only the Dashboard and Receivers', async ({ page }) => {
    await page.goto('/app/');
    const nav = page.getByRole('navigation', { name: 'Sections' });
    await expect(nav.getByRole('link', { name: 'Dashboard' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Receivers' })).toBeVisible();
    for (const hidden of ['Vehicles', 'Volunteers', 'Convoys', 'Boxes', 'Manifests']) {
      await expect(nav.getByRole('link', { name: hidden })).toBeHidden();
    }
  });
});
