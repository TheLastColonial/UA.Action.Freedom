import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import { makeVehicle } from '../../test/factories/vehicle';
import { vehicleApi } from '../../test/msw/vehicles';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { vehicleRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'vehicles', children: vehicleRoutes },
];

test('renders the vehicle from the detail endpoint', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X', plate: 'ZZ99 ZZZ' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X',
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('heading', { name: 'VIN-X' })).toBeInTheDocument();
  await expect.element(screen.getByText('ZZ99 ZZZ')).toBeInTheDocument();
});

test('renders cargo capacity when it has been recorded', async () => {
  worker.use(
    ...vehicleApi([
      makeVehicle({
        vin: 'VIN-X',
        maxCargoWeightKg: 900.5,
        cargoWidthCm: 150.25,
        cargoDepthCm: 300,
        cargoHeightCm: 180.75,
      }),
    ]).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X',
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('900.5 kg')).toBeInTheDocument();
  await expect.element(screen.getByText('150.25 × 300 × 180.75 cm')).toBeInTheDocument();
});

test('renders a dash for cargo capacity that has never been measured', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-Y' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-Y',
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('heading', { name: 'VIN-Y' })).toBeInTheDocument();
  await expect.element(screen.getByText('Maximum cargo weight')).toBeInTheDocument();
  await expect.element(screen.getByText('Cargo dimensions (W × D × H)')).toBeInTheDocument();
});

test('groups fields into named cards', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X',
    roles: ['Loader'],
  });

  for (const name of [
    'Vehicle details',
    'Engine details',
    'Cargo capacity',
    'Status',
    'Purchase information',
    'Notes',
  ]) {
    await expect.element(screen.getByRole('region', { name })).toBeInTheDocument();
  }
});

test('renders purchase information when recorded', async () => {
  worker.use(
    ...vehicleApi([
      makeVehicle({
        vin: 'VIN-X',
        purchaserName: 'A. Buyer',
        purchaseDate: '2026-01-15T00:00:00Z',
      }),
    ]).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X',
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('A. Buyer')).toBeInTheDocument();
  await expect.element(screen.getByText('2026-01-15')).toBeInTheDocument();
});

test('renders Not found for a VIN that does not exist', async () => {
  worker.use(...vehicleApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/UNKNOWN',
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('shows the inspection result whether or not the vehicle is in for servicing', async () => {
  worker.use(
    ...vehicleApi([
      makeVehicle({
        vin: 'VIN-X',
        servicing: false,
        inspectionStatus: 'Failed',
        inspectionNotes: 'Cracked windscreen',
      }),
    ]).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X',
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('Inspection status')).toBeInTheDocument();
  await expect.element(screen.getByText('Issues found')).toBeInTheDocument();
  await expect.element(screen.getByText('Cracked windscreen')).toBeInTheDocument();
});

test.each<[Role]>([['Mechanic'], ['Administrator']])(
  'links a %s to the servicing page',
  async (role) => {
    worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X' })]).handlers);

    const screen = await renderWithProviders(null, {
      routes,
      route: '/vehicles/VIN-X',
      roles: [role],
    });

    await expect
      .element(screen.getByRole('link', { name: 'Servicing' }))
      .toHaveAttribute('href', '/vehicles/VIN-X/servicing');
  },
);

test.each<[Role]>([['Loader'], ['Purchaser'], ['Dispatcher']])(
  'offers a %s no servicing link',
  async (role) => {
    worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X' })]).handlers);

    const screen = await renderWithProviders(null, {
      routes,
      route: '/vehicles/VIN-X',
      roles: [role],
    });

    await expect.element(screen.getByRole('heading', { name: 'VIN-X' })).toBeInTheDocument();
    await expect.element(screen.getByRole('link', { name: 'Servicing' })).not.toBeInTheDocument();
  },
);

test('a mechanic may edit a vehicle', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X',
    roles: ['Mechanic'],
  });

  await expect.element(screen.getByRole('link', { name: 'Edit' })).toBeInTheDocument();
});

test('hides Edit and Delete from a read-only role', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X',
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('heading', { name: 'VIN-X' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Delete' })).not.toBeInTheDocument();
});

test('deletes the vehicle and returns to the list', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X',
    roles: ['Purchaser'],
  });

  await screen.getByRole('button', { name: 'Delete' }).click();

  await expect.element(screen.getByRole('heading', { name: 'Vehicles' })).toBeInTheDocument();
  await expect.element(screen.getByText('No vehicles recorded yet.')).toBeInTheDocument();
});

test('a vehicle handed over in Ukraine says so', async () => {
  worker.use(
    ...vehicleApi([makeVehicle({ vin: 'VIN-X', handedOverAt: '2026-06-05T17:00:00' })]).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X',
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('Handed over on 2026-06-05')).toBeInTheDocument();
});
