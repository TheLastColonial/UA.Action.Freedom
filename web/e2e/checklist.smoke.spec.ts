import { devices, expect, test } from '@playwright/test';

import { signIn } from './auth';
import { departedConvoyLedBy } from './departedConvoy';
import { stackIsUp } from './stack';

// A phone, because the checklist is a phone page (ADR 0016, X10).
test.use({ ...devices['Pixel 7'] });

test.beforeEach(async () => {
  test.skip(!(await stackIsUp()), 'the local stack is not up (docker compose + tofu apply)');
});

test('@smoke the leader marks a stop reached and a border crossed on a phone, HQ sees both, and nothing is stored', async ({
  page,
}) => {
  const convoyId = await departedConvoyLedBy('leader');
  const noStore: { url: string; cacheControl: string | undefined }[] = [];
  page.on('response', (response) => {
    if (/\/convoys\/\d+\/(progress|route\/\d+\/(reached|crossed))$/.test(response.url())) {
      noStore.push({ url: response.url(), cacheControl: response.headers()['cache-control'] });
    }
  });

  await signIn(page, 'leader');
  await page.goto(`/app/convoys/${String(convoyId)}/checklist`);
  await expect(page.getByRole('heading', { name: 'Checklist' })).toBeVisible();

  await page.getByRole('button', { name: 'Reached Coventry depot' }).click();
  await expect(page.getByText(/^Reached at \d\d:\d\d, /)).toBeVisible();

  await page.getByRole('button', { name: 'Crossed Calais border' }).click();
  await expect(page.getByText(/^Crossed at \d\d:\d\d, /)).toBeVisible();

  // No address is on the page: a point is its name and its kind.
  await expect(page.getByText('Border', { exact: true })).toBeVisible();

  // Nothing is kept on the device.
  const stored = await page.evaluate(() => ({
    local: localStorage.length,
    session: sessionStorage.length,
  }));
  expect(stored.local).toBe(0);
  expect(stored.session).toBe(0);
  const registrations = await page.evaluate(
    async () => (await navigator.serviceWorker.getRegistrations()).length,
  );
  expect(registrations).toBe(0);

  expect(noStore.length).toBeGreaterThan(0);
  for (const { cacheControl } of noStore) {
    expect(cacheControl).toContain('no-store');
  }

  // HQ's overview shows what was reported.
  await signIn(page, 'operator');
  await page.goto(`/app/convoys/${String(convoyId)}`);
  await expect(page.getByText(/Reached 20\d\d-\d\d-\d\d \d\d:\d\d/)).toBeVisible();
  await expect(page.getByText(/Crossed 20\d\d-\d\d-\d\d \d\d:\d\d/)).toBeVisible();
});

test('@smoke a dispatcher marks the next point on the leaders behalf and the record names them', async ({
  page,
}) => {
  const convoyId = await departedConvoyLedBy('leader');

  await signIn(page, 'operator');
  await page.goto(`/app/convoys/${String(convoyId)}/checklist`);
  await page.getByRole('button', { name: 'Reached Coventry depot' }).click();

  await expect(page.getByText(/entered by .+ for the leader/)).toBeVisible();
});
