import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeConvoy } from '../../test/factories/convoy';
import { makeManifest } from '../../test/factories/manifest';
import { convoyApi } from '../../test/msw/convoys';
import { manifestApi } from '../../test/msw/manifests';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ManifestStatePanel } from './ManifestStatePanel';

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('from Created without a convoy: Reject is offered, Propose is disabled with a reason', async () => {
  worker.use(...manifestApi([makeManifest({ id: 'M1', status: 'Created' })]).handlers);

  const screen = renderWithProviders(<ManifestStatePanel manifest={makeManifest({ id: 'M1' })} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByRole('button', { name: 'Reject' })).toBeEnabled();
  await expect.element(screen.getByRole('button', { name: 'Propose' })).toBeDisabled();
  await expect
    .element(screen.getByText('Link this manifest to a convoy before proposing it.'))
    .toBeInTheDocument();
});

test('propose is enabled and moves the manifest to Proposed when the convoy is published', async () => {
  const manifest = makeManifest({ id: 'M2', status: 'Created', convoyId: 7 });
  const mApi = manifestApi([manifest], { publishedConvoyIds: [7] });
  worker.use(
    ...mApi.handlers,
    ...convoyApi([makeConvoy({ id: 7, truckListPublished: true })]).handlers,
  );

  const screen = renderWithProviders(<ManifestStatePanel manifest={manifest} />, {
    roles: ['Dispatcher'],
  });

  const propose = screen.getByRole('button', { name: 'Propose' });
  await expect.element(propose).toBeEnabled();
  await propose.click();

  await expect.poll(() => mApi.db.get('M2')?.status).toBe('Proposed');
});

test('approve is hidden from a Dispatcher and shown to an Administrator', async () => {
  const manifest = makeManifest({ id: 'M3', status: 'Proposed', convoyId: 7 });
  worker.use(
    ...manifestApi([manifest], { publishedConvoyIds: [7] }).handlers,
    ...convoyApi([makeConvoy({ id: 7, truckListPublished: true })]).handlers,
  );

  const asDispatcher = renderWithProviders(<ManifestStatePanel manifest={manifest} />, {
    roles: ['Dispatcher'],
  });
  await expect
    .element(asDispatcher.getByRole('button', { name: 'Approve' }))
    .not.toBeInTheDocument();

  const asAdmin = renderWithProviders(<ManifestStatePanel manifest={manifest} />, {
    roles: ['Administrator'],
  });
  await expect.element(asAdmin.getByRole('button', { name: 'Approve' })).toBeInTheDocument();
});

test('approving shows the GMR-submitted confirmation and freezes the manifest', async () => {
  const manifest = makeManifest({ id: 'M4', status: 'Proposed', convoyId: 7 });
  const mApi = manifestApi([manifest], { publishedConvoyIds: [7] });
  worker.use(
    ...mApi.handlers,
    ...convoyApi([makeConvoy({ id: 7, truckListPublished: true })]).handlers,
  );

  const screen = renderWithProviders(<ManifestStatePanel manifest={manifest} />, {
    roles: ['Administrator'],
  });

  await screen.getByRole('button', { name: 'Approve' }).click();

  await expect
    .element(screen.getByText('GMR submitted — the manifest is now frozen.'))
    .toBeInTheDocument();
  expect(mApi.db.get('M4')?.frozen).toBe(true);
});

test('an illegal transition surfaces the API detail', async () => {
  // Ready has no legal edge from Created; force it by seeding the panel with a stale status.
  const stale = makeManifest({ id: 'M5', status: 'Ready' });
  const server = makeManifest({ id: 'M5', status: 'Created' });
  worker.use(...manifestApi([server]).handlers);

  const screen = renderWithProviders(<ManifestStatePanel manifest={stale} />, {
    roles: ['Dispatcher'],
  });

  await screen.getByRole('button', { name: 'Depart' }).click();

  await expect
    .element(screen.getByText('A manifest cannot move to that state from the one it is in.'))
    .toBeInTheDocument();
});
