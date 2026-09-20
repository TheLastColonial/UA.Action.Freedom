import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makeManifest } from '../../test/factories/manifest';
import { manifestApi } from '../../test/msw/manifests';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { convoyRoutes } from '../convoys/routes';
import { manifestRoutes } from './routes';

// A manifest is opened against a truck-list entry, so its route lives under the convoy — the
// same shape as POST /convoys/{id}/vehicles/{vin}/manifest.
const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'convoys', children: convoyRoutes },
  { path: 'manifests', children: manifestRoutes },
];

const NEW = '/convoys/7/vehicles/VIN-1/manifest/new';

test('requires a reference', async () => {
  worker.use(...manifestApi([]).handlers);
  const screen = await renderWithProviders(null, {
    routes,
    route: NEW,
    roles: ['Dispatcher'],
  });

  await screen.getByRole('button', { name: 'Open manifest' }).click();
  await expect.element(screen.getByText('A manifest reference is required')).toBeInTheDocument();
});

test('surfaces a duplicate-reference 409 and stays on the form', async () => {
  worker.use(
    ...manifestApi([makeManifest({ id: 'UA-DUP', convoyId: 9, vin: 'VIN-OTHER' })]).handlers,
  );
  const screen = await renderWithProviders(null, {
    routes,
    route: NEW,
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('Reference').fill('UA-DUP');
  await screen.getByRole('button', { name: 'Open manifest' }).click();

  await expect
    .element(screen.getByText("A manifest with reference 'UA-DUP' already exists."))
    .toBeInTheDocument();
  await expect
    .element(screen.getByRole('heading', { name: 'New manifest for VIN-1' }))
    .toBeInTheDocument();
});

test('refuses a second manifest for the same vehicle on the same convoy', async () => {
  // Arrival asks each vehicle for its finished manifest and has to get one answer.
  worker.use(
    ...manifestApi([makeManifest({ id: 'UA-FIRST', convoyId: 7, vin: 'VIN-1' })]).handlers,
  );
  const screen = await renderWithProviders(null, {
    routes,
    route: NEW,
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('Reference').fill('UA-SECOND');
  await screen.getByRole('button', { name: 'Open manifest' }).click();

  await expect
    .element(screen.getByText("Vehicle 'VIN-1' already has a manifest on this convoy."))
    .toBeInTheDocument();
});

test('opens the manifest against the convoy and vehicle in the route', async () => {
  const api = manifestApi([]);
  worker.use(...api.handlers);
  const screen = await renderWithProviders(null, {
    routes,
    route: NEW,
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('Reference').fill('UA-NEW-1');
  await screen.getByRole('button', { name: 'Open manifest' }).click();

  await expect.element(screen.getByRole('heading', { name: 'UA-NEW-1' })).toBeInTheDocument();
  expect(api.db.get('UA-NEW-1')).toMatchObject({ convoyId: 7, vin: 'VIN-1' });
});
