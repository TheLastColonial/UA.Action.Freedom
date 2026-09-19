import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makeConvoy, makeConvoyVehicle } from '../../test/factories/convoy';
import { makeVehicle } from '../../test/factories/vehicle';
import { convoyApi } from '../../test/msw/convoys';
import { vehicleApi } from '../../test/msw/vehicles';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { convoyRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'convoys', children: convoyRoutes },
];

test('shows the overview and Not found for an unknown id', async () => {
  worker.use(...convoyApi([makeConvoy({ id: 7 })]).handlers);

  const found = await renderWithProviders(null, { routes, route: '/convoys/7', roles: ['Loader'] });
  await expect.element(found.getByRole('heading', { name: 'Convoy #7' })).toBeInTheDocument();

  const missing = await renderWithProviders(null, {
    routes,
    route: '/convoys/999',
    roles: ['Loader'],
  });
  await expect.element(missing.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('groups overview fields into a named card', async () => {
  worker.use(...convoyApi([makeConvoy({ id: 7 })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/convoys/7',
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('region', { name: 'Convoy details' })).toBeInTheDocument();
});

test('publishing the truck list flips the badge and removes the button', async () => {
  worker.use(...convoyApi([makeConvoy({ id: 7, truckListPublished: false })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/convoys/7',
    roles: ['Dispatcher'],
  });

  await screen.getByRole('button', { name: 'Publish truck list' }).click();

  await expect.element(screen.getByText('Truck list published')).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Publish truck list' }))
    .not.toBeInTheDocument();
});

test('a Dispatcher can open the Route and Vehicles tabs', async () => {
  worker.use(...convoyApi([makeConvoy({ id: 7 })]).handlers, ...vehicleApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/convoys/7',
    roles: ['Dispatcher'],
  });

  await screen.getByRole('tab', { name: 'Route' }).click();
  await expect.element(screen.getByRole('button', { name: 'Add stop' })).toBeInTheDocument();

  await screen.getByRole('tab', { name: 'Vehicles' }).click();
  await expect.element(screen.getByRole('combobox', { name: 'Vehicle' })).toBeInTheDocument();
});

test('hides publish from a role without convoys:write', async () => {
  worker.use(...convoyApi([makeConvoy({ id: 7 })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/convoys/7',
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Convoy #7' })).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Publish truck list' }))
    .not.toBeInTheDocument();
});

test('a dispatcher marks a convoy arrived once every vehicle has a finished manifest', async () => {
  const fleet = vehicleApi([makeVehicle({ vin: 'VIN-A', inspectionStatus: 'Passed' })]);
  const convoys = convoyApi([makeConvoy({ id: 7, truckListPublished: true })], {
    fleet: fleet.db,
    manifestStatusByVin: new Map([['VIN-A', 'Delivered']]),
  });
  convoys.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-A' })]);
  worker.use(...convoys.handlers, ...fleet.handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/convoys/7',
    roles: ['Dispatcher'],
  });
  await screen.getByRole('button', { name: 'Mark arrived' }).click();

  await expect.element(screen.getByText('Arrived', { exact: true })).toBeInTheDocument();
  expect(convoys.db.get(7)?.arrived).toBe(true);
  expect(fleet.db.get('VIN-A')?.handedOverAt).not.toBeNull();
});

test('marking arrival names the vehicles still on the road', async () => {
  const convoys = convoyApi([makeConvoy({ id: 7, truckListPublished: true })], {
    manifestStatusByVin: new Map([['VIN-A', 'InTransit']]),
  });
  convoys.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-A' })]);
  worker.use(...convoys.handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/convoys/7',
    roles: ['Dispatcher'],
  });
  await screen.getByRole('button', { name: 'Mark arrived' }).click();

  await expect.element(screen.getByRole('alert')).toHaveTextContent('VIN-A');
  expect(convoys.db.get(7)?.arrived).toBe(false);
});

test('arrival is not offered before the truck list is published, nor to a loader', async () => {
  worker.use(
    ...convoyApi([makeConvoy({ id: 7 }), makeConvoy({ id: 8, truckListPublished: true })]).handlers,
  );

  const open = await renderWithProviders(null, {
    routes,
    route: '/convoys/7',
    roles: ['Dispatcher'],
  });
  await expect.element(open.getByRole('heading', { name: 'Convoy #7' })).toBeInTheDocument();
  await expect.element(open.getByRole('button', { name: 'Mark arrived' })).not.toBeInTheDocument();

  const loader = await renderWithProviders(null, {
    routes,
    route: '/convoys/8',
    roles: ['Loader'],
  });
  await expect.element(loader.getByRole('heading', { name: 'Convoy #8' })).toBeInTheDocument();
  await expect
    .element(loader.getByRole('button', { name: 'Mark arrived' }))
    .not.toBeInTheDocument();
});
