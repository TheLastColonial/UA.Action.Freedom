import { expect, test } from 'vitest';

import { makeConvoy } from '../../test/factories/convoy';
import { makeManifest } from '../../test/factories/manifest';
import { convoyApi } from '../../test/msw/convoys';
import { manifestApi } from '../../test/msw/manifests';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ManifestStatePanel } from './ManifestStatePanel';

test('from Rejected on an unpublished truck list: Propose is disabled with a reason', async () => {
  worker.use(...manifestApi([makeManifest({ id: 'M1', status: 'Rejected' })]).handlers);

  const screen = await renderWithProviders(
    <ManifestStatePanel manifest={makeManifest({ id: 'M1', status: 'Rejected' })} />,
    {
      roles: ['Dispatcher'],
    },
  );

  await expect.element(screen.getByRole('button', { name: 'Propose' })).toBeDisabled();
  await expect
    .element(screen.getByText("The convoy's truck list has not been published yet."))
    .toBeInTheDocument();
});

test('propose is enabled and moves the manifest to Proposed when the convoy is published', async () => {
  const manifest = makeManifest({ id: 'M2', status: 'Rejected', convoyId: 7 });
  const mApi = manifestApi([manifest], { publishedConvoyIds: [7] });
  worker.use(
    ...mApi.handlers,
    ...convoyApi([makeConvoy({ id: 7, truckListPublished: true })]).handlers,
  );

  const screen = await renderWithProviders(<ManifestStatePanel manifest={manifest} />, {
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

  const asDispatcher = await renderWithProviders(<ManifestStatePanel manifest={manifest} />, {
    roles: ['Dispatcher'],
  });
  await expect
    .element(asDispatcher.getByRole('button', { name: 'Approve' }))
    .not.toBeInTheDocument();

  const asAdmin = await renderWithProviders(<ManifestStatePanel manifest={manifest} />, {
    roles: ['Administrator'],
  });
  await expect.element(asAdmin.getByRole('button', { name: 'Approve' })).toBeInTheDocument();
});

test('approving shows the signed-off confirmation and approves the manifest', async () => {
  const manifest = makeManifest({ id: 'M4', status: 'Proposed', convoyId: 7 });
  const mApi = manifestApi([manifest], { publishedConvoyIds: [7] });
  worker.use(
    ...mApi.handlers,
    ...convoyApi([makeConvoy({ id: 7, truckListPublished: true })]).handlers,
  );

  const screen = await renderWithProviders(<ManifestStatePanel manifest={manifest} />, {
    roles: ['Administrator'],
  });

  await screen.getByRole('button', { name: 'Approve' }).click();

  await expect
    .element(screen.getByText('Load signed off. Declarations are filed from the Declarations tab.'))
    .toBeInTheDocument();
  expect(mApi.db.get('M4')?.status).toBe('Approved');
  expect(mApi.db.get('M4')?.approved).toBe(true);
});

test('an illegal transition surfaces the API detail', async () => {
  // Reject has no legal edge from Approved; force it by seeding the panel with a stale status.
  const stale = makeManifest({ id: 'M5', status: 'Proposed' });
  const server = makeManifest({ id: 'M5', status: 'Approved', approved: true });
  worker.use(...manifestApi([server]).handlers);

  const screen = await renderWithProviders(<ManifestStatePanel manifest={stale} />, {
    roles: ['Dispatcher'],
  });

  await screen.getByRole('button', { name: 'Reject' }).click();

  await expect
    .element(screen.getByText('A manifest cannot move to that state from the one it is in.'))
    .toBeInTheDocument();
});

test('a reopened manifest says why it is back in front of the approver', async () => {
  const manifest = makeManifest({
    id: 'M6',
    status: 'Proposed',
    reopenedAt: '2026-09-02T10:00:00',
  });
  worker.use(...manifestApi([manifest]).handlers);

  const screen = await renderWithProviders(<ManifestStatePanel manifest={manifest} />, {
    roles: ['Administrator'],
  });

  await expect.element(screen.getByText(/Reopened by a load change/)).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Approve' })).toBeEnabled();
});

test('a manifest has no depart button: a convoy departs as a whole', async () => {
  const manifest = makeManifest({ id: 'M9', status: 'Approved', convoyId: 7, vin: 'VIN-9' });
  worker.use(...manifestApi([manifest], { publishedConvoyIds: [7] }).handlers);

  const screen = await renderWithProviders(<ManifestStatePanel manifest={manifest} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByRole('button', { name: 'Depart' })).not.toBeInTheDocument();
});
