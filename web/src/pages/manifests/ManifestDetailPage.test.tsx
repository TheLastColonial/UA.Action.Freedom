import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeConvoy } from '../../test/factories/convoy';
import { makeManifest } from '../../test/factories/manifest';
import { convoyApi } from '../../test/msw/convoys';
import { manifestApi } from '../../test/msw/manifests';
import { personApi } from '../../test/msw/people';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { manifestRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'manifests', children: manifestRoutes },
];

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('renders the overview and Not found for an unknown reference', async () => {
  worker.use(...manifestApi([makeManifest({ id: 'D1', vin: 'VIN9' })]).handlers);

  const found = renderWithProviders(null, { routes, route: '/manifests/D1', roles: ['Loader'] });
  await expect.element(found.getByRole('heading', { name: 'D1' })).toBeInTheDocument();
  await expect.element(found.getByText('VIN9')).toBeInTheDocument();

  const missing = renderWithProviders(null, {
    routes,
    route: '/manifests/NOPE',
    roles: ['Loader'],
  });
  await expect.element(missing.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('a frozen manifest offers no Edit link and the edit page explains why', async () => {
  worker.use(
    ...manifestApi([makeManifest({ id: 'D2', frozen: true, status: 'Confirmed' })]).handlers,
  );

  const detail = renderWithProviders(null, {
    routes,
    route: '/manifests/D2',
    roles: ['Dispatcher'],
  });
  await expect.element(detail.getByRole('heading', { name: 'D2' })).toBeInTheDocument();
  await expect.element(detail.getByRole('link', { name: 'Edit' })).not.toBeInTheDocument();

  const edit = renderWithProviders(null, {
    routes,
    route: '/manifests/D2/edit',
    roles: ['Dispatcher'],
  });
  await expect
    .element(
      edit.getByText(
        'A Goods Movement Reference has been created for this manifest — it can no longer be edited.',
      ),
    )
    .toBeInTheDocument();
});

test('the tabs open the Status, Teams, Cargo and Weight panels', async () => {
  worker.use(
    ...manifestApi([makeManifest({ id: 'D3', convoyId: 7 })], { publishedConvoyIds: [7] }).handlers,
    ...convoyApi([makeConvoy({ id: 7, truckListPublished: true })]).handlers,
    ...personApi([]).handlers,
  );

  const screen = renderWithProviders(null, {
    routes,
    route: '/manifests/D3',
    roles: ['Dispatcher'],
  });

  await screen.getByRole('button', { name: 'Status' }).click();
  await expect.element(screen.getByRole('heading', { name: /Status:/ })).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Teams' }).click();
  await expect.element(screen.getByRole('heading', { name: 'Driver teams' })).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Cargo' }).click();
  await expect.element(screen.getByRole('heading', { name: 'Cargo' })).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Weight' }).click();
  await expect
    .element(screen.getByRole('heading', { name: 'Border-check weight' }))
    .toBeInTheDocument();
});
