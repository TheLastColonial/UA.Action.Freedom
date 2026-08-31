import { HttpResponse, http } from 'msw';
import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeVehicle } from '../../test/factories/vehicle';
import { vehicleApi } from '../../test/msw/vehicles';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { vehicleRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'vehicles', children: vehicleRoutes },
];

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('renders a row per vehicle from the list endpoint', async () => {
  const seed = [makeVehicle({ vin: 'VIN-A' }), makeVehicle({ vin: 'VIN-B' })];
  worker.use(...vehicleApi(seed).handlers);

  const screen = renderWithProviders(null, { routes, route: '/vehicles', roles: ['Purchaser'] });

  await expect.element(screen.getByRole('link', { name: 'VIN-A' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'VIN-B' })).toBeInTheDocument();
});

test('shows an empty message when there are no vehicles', async () => {
  worker.use(...vehicleApi([]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/vehicles', roles: ['Purchaser'] });

  await expect.element(screen.getByText('No vehicles recorded yet.')).toBeInTheDocument();
});

test('shows an error message when the list cannot be loaded', async () => {
  worker.use(http.get('/vehicles', () => new HttpResponse(null, { status: 500 })));

  const screen = renderWithProviders(null, { routes, route: '/vehicles', roles: ['Purchaser'] });

  await expect.element(screen.getByRole('alert')).toBeInTheDocument();
});

test('hides "New vehicle" from a read-only role and shows it to a writer', async () => {
  worker.use(...vehicleApi([]).handlers);

  const asLoader = renderWithProviders(null, { routes, route: '/vehicles', roles: ['Loader'] });
  await expect.element(asLoader.getByText('No vehicles recorded yet.')).toBeInTheDocument();
  await expect.element(asLoader.getByRole('link', { name: 'New vehicle' })).not.toBeInTheDocument();

  const asPurchaser = renderWithProviders(null, {
    routes,
    route: '/vehicles',
    roles: ['Purchaser'],
  });
  await expect.element(asPurchaser.getByRole('link', { name: 'New vehicle' })).toBeInTheDocument();
});

test('offers the next page only while the current page is full', async () => {
  const fullPage = Array.from({ length: 50 }, (_v, i) => makeVehicle({ vin: `VIN-${String(i)}` }));
  worker.use(...vehicleApi(fullPage).handlers);

  const screen = renderWithProviders(null, { routes, route: '/vehicles', roles: ['Purchaser'] });

  const next = screen.getByRole('button', { name: 'Next' });
  await expect.element(next).toBeEnabled();

  await next.click();
  await expect.element(screen.getByText('Page 2')).toBeInTheDocument();
});
