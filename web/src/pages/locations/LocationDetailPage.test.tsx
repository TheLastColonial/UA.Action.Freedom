import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeBay, makeLocation } from '../../test/factories/location';
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

test('renders a known location', async () => {
  worker.use(...locationApi([makeLocation({ id: 3, name: 'Coventry Depot' })]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/locations/3', roles: ['Loader'] });
  await expect.element(screen.getByRole('heading', { name: 'Coventry Depot' })).toBeInTheDocument();
});

test('an unknown location is a Not found page', async () => {
  worker.use(...locationApi([]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/locations/99', roles: ['Loader'] });
  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('lists the bays at a location', async () => {
  worker.use(
    ...locationApi(
      [makeLocation({ id: 3, name: 'Coventry Depot' })],
      [makeBay({ id: 9, locationId: 3, code: 'A1' })],
    ).handlers,
  );

  const screen = renderWithProviders(null, { routes, route: '/locations/3', roles: ['Loader'] });

  await expect.element(screen.getByText('A1')).toBeInTheDocument();
});

test('an administrator adds a bay and then removes it', async () => {
  worker.use(...locationApi([makeLocation({ id: 3, name: 'Coventry Depot' })]).handlers);

  const screen = renderWithProviders(null, {
    routes,
    route: '/locations/3',
    roles: ['Administrator'],
  });

  await expect.element(screen.getByText('No bays at this location yet.')).toBeInTheDocument();

  await screen.getByLabelText('Bay code').fill('A1');
  await screen.getByRole('button', { name: 'Add bay' }).click();

  await expect.element(screen.getByText('A1')).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Remove' }).click();

  await expect.element(screen.getByText('No bays at this location yet.')).toBeInTheDocument();
});

test('a loader cannot add a bay', async () => {
  worker.use(...locationApi([makeLocation({ id: 3, name: 'Coventry Depot' })]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/locations/3', roles: ['Loader'] });

  await expect.element(screen.getByLabelText('Bay code')).not.toBeInTheDocument();
});
