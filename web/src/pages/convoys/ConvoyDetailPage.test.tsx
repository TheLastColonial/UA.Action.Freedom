import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeConvoy } from '../../test/factories/convoy';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { convoyRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'convoys', children: convoyRoutes },
];

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('shows the overview and Not found for an unknown id', async () => {
  worker.use(...convoyApi([makeConvoy({ id: 7 })]).handlers);

  const found = renderWithProviders(null, { routes, route: '/convoys/7', roles: ['Loader'] });
  await expect.element(found.getByRole('heading', { name: 'Convoy #7' })).toBeInTheDocument();

  const missing = renderWithProviders(null, {
    routes,
    route: '/convoys/999',
    roles: ['Loader'],
  });
  await expect.element(missing.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('publishing the truck list flips the badge and removes the button', async () => {
  worker.use(...convoyApi([makeConvoy({ id: 7, truckListPublished: false })]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/convoys/7', roles: ['Dispatcher'] });

  await screen.getByRole('button', { name: 'Publish truck list' }).click();

  await expect.element(screen.getByText('Truck list published')).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Publish truck list' }))
    .not.toBeInTheDocument();
});

test('a Dispatcher can open the Route and Vehicles tabs', async () => {
  worker.use(...convoyApi([makeConvoy({ id: 7 })]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/convoys/7', roles: ['Dispatcher'] });

  await screen.getByRole('button', { name: 'Route' }).click();
  await expect.element(screen.getByRole('button', { name: 'Add stop' })).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Vehicles' }).click();
  await expect.element(screen.getByRole('button', { name: 'Assign vehicle' })).toBeInTheDocument();
});

test('hides publish from a role without convoys:write', async () => {
  worker.use(...convoyApi([makeConvoy({ id: 7 })]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/convoys/7', roles: ['Loader'] });

  await expect.element(screen.getByRole('heading', { name: 'Convoy #7' })).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Publish truck list' }))
    .not.toBeInTheDocument();
});
