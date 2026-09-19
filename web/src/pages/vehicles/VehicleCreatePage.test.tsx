import { http } from 'msw';
import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makeVehicle } from '../../test/factories/vehicle';
import { validationProblem } from '../../test/msw/problem';
import { vehicleApi } from '../../test/msw/vehicles';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { vehicleRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'vehicles', children: vehicleRoutes },
];

async function fillMinimum(screen: Awaited<ReturnType<typeof renderWithProviders>>): Promise<void> {
  await screen.getByLabelText('VIN').fill('WVWZZZ1KZAW000009');
  await screen.getByLabelText('Number plate').fill('AB12 CDE');
  await screen.getByLabelText('Year').fill('2016');
  await screen.getByLabelText('Kerb weight (kg)').fill('1900');
}

test('renders a labelled field for every part of the request', async () => {
  worker.use(...vehicleApi([]).handlers);
  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/new',
    roles: ['Purchaser'],
  });

  for (const label of [
    'VIN',
    'Number plate',
    'Make',
    'Model',
    'Colour',
    'Year',
    'Transmission',
    'Fuel',
    'Kerb weight (kg)',
    'Mileage',
    'Maximum weight (kg)',
    'Width (cm)',
    'Depth (cm)',
    'Height (cm)',
    'Purchaser',
    'Purchase date',
    'Notes',
  ]) {
    await expect.element(screen.getByLabelText(label)).toBeInTheDocument();
  }
});

test('groups fields into named cards and drops Convoy id and servicing', async () => {
  worker.use(...vehicleApi([]).handlers);
  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/new',
    roles: ['Purchaser'],
  });

  for (const name of [
    'Vehicle details',
    'Engine details',
    'Cargo capacity',
    'Purchase information',
    'Notes',
  ]) {
    await expect.element(screen.getByRole('group', { name })).toBeInTheDocument();
  }

  await expect.element(screen.getByLabelText('Convoy id')).not.toBeInTheDocument();
  await expect.element(screen.getByLabelText('In for servicing')).not.toBeInTheDocument();
});

test('shows client-side validation messages on an invalid submit', async () => {
  worker.use(...vehicleApi([]).handlers);
  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/new',
    roles: ['Purchaser'],
  });

  await screen.getByRole('button', { name: 'Create vehicle' }).click();

  await expect.element(screen.getByText('VIN is required')).toBeInTheDocument();
  await expect.element(screen.getByText('Number plate is required')).toBeInTheDocument();
});

test('maps a 400 problem+json error onto the named field', async () => {
  worker.use(
    http.post('/vehicles', () => validationProblem({ Plate: ['That plate is already on file.'] })),
  );
  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/new',
    roles: ['Purchaser'],
  });

  await fillMinimum(screen);
  await screen.getByRole('button', { name: 'Create vehicle' }).click();

  await expect.element(screen.getByText('That plate is already on file.')).toBeInTheDocument();
});

test('creates the vehicle and navigates to it, reading the id from Location', async () => {
  worker.use(...vehicleApi([]).handlers);
  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/new',
    roles: ['Purchaser'],
  });

  await fillMinimum(screen);
  await screen.getByRole('button', { name: 'Create vehicle' }).click();

  await expect
    .element(screen.getByRole('heading', { name: 'WVWZZZ1KZAW000009' }))
    .toBeInTheDocument();
});

test('surfaces a 409 detail verbatim and stays on the form', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'WVWZZZ1KZAW000009' })]).handlers);
  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/new',
    roles: ['Purchaser'],
  });

  await fillMinimum(screen);
  await screen.getByRole('button', { name: 'Create vehicle' }).click();

  await expect
    .element(screen.getByText("A vehicle with VIN 'WVWZZZ1KZAW000009' already exists."))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('heading', { name: 'New vehicle' })).toBeInTheDocument();
});
