import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeLocation } from '../../test/factories/location';
import { locationApi } from '../../test/msw/locations';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { locationRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'locations', children: locationRoutes },
];

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('lists locations by name', async () => {
  worker.use(
    ...locationApi([
      makeLocation({ name: 'Coventry Depot' }),
      makeLocation({ name: 'London Warehouse' }),
    ]).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/locations',
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByRole('link', { name: 'Coventry Depot' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'London Warehouse' })).toBeInTheDocument();
});

test('only an administrator sees "New location"', async () => {
  worker.use(...locationApi([]).handlers);

  const asLoader = await renderWithProviders(null, {
    routes,
    route: '/locations',
    roles: ['Loader'],
  });
  await expect.element(asLoader.getByText('No locations recorded yet.')).toBeInTheDocument();
  await expect
    .element(asLoader.getByRole('link', { name: 'New location' }))
    .not.toBeInTheDocument();

  const asAdmin = await renderWithProviders(null, {
    routes,
    route: '/locations',
    roles: ['Administrator'],
  });
  await expect.element(asAdmin.getByRole('link', { name: 'New location' })).toBeInTheDocument();
});
