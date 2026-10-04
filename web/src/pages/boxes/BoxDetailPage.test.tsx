import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makeBox } from '../../test/factories/box';
import { makeBay, makeLocation } from '../../test/factories/location';
import { boxApi } from '../../test/msw/boxes';
import { locationApi } from '../../test/msw/locations';
import { meApi } from '../../test/msw/me';
import { personApi } from '../../test/msw/people';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { boxRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'boxes', children: boxRoutes },
];

test('renders the box and Not found for an unknown id', async () => {
  worker.use(...boxApi([makeBox({ id: 4 })]).handlers);

  const found = await renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });
  await expect.element(found.getByRole('heading', { name: 'Box #4' })).toBeInTheDocument();

  const missing = await renderWithProviders(null, {
    routes,
    route: '/boxes/99',
    roles: ['Loader'],
  });
  await expect.element(missing.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('groups fields into a named card', async () => {
  worker.use(...boxApi([makeBox({ id: 4 })]).handlers);

  const screen = await renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await expect.element(screen.getByRole('region', { name: 'Box details' })).toBeInTheDocument();
});

test('a Dispatcher can pack a box but sees no validate panel', async () => {
  worker.use(...boxApi([makeBox({ id: 4 })]).handlers, ...personApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/boxes/4',
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Contents' })).toBeInTheDocument();
  await expect
    .element(screen.getByRole('heading', { name: 'Validate this box' }))
    .not.toBeInTheDocument();
});

test('validating the box freezes it: panel gone, contents fixed, no Edit', async () => {
  worker.use(...boxApi([makeBox({ id: 4 })]).handlers, ...meApi());

  const screen = await renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await expect.element(screen.getByText('Val Checker')).toBeInTheDocument();
  await screen.getByLabelText('Confirmed weight (kg)').fill('18');
  await screen.getByRole('button', { name: 'Validate box' }).click();

  await expect.element(screen.getByText('Validated', { exact: true })).toBeInTheDocument();
  await expect
    .element(screen.getByRole('heading', { name: 'Validate this box' }))
    .not.toBeInTheDocument();
  await expect
    .element(screen.getByText('This box has been validated — its contents are now fixed.'))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
});

test('rejects a confirmed weight outside 1..500', async () => {
  worker.use(...boxApi([makeBox({ id: 4 })]).handlers, ...meApi());

  const screen = await renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await screen.getByLabelText('Confirmed weight (kg)').fill('750');
  await screen.getByRole('button', { name: 'Validate box' }).click();

  await expect
    .element(screen.getByText("'Weight' must be a whole number between 1 and 500"))
    .toBeInTheDocument();
});

test('a Dispatcher sees the box bay but not the controls to change it', async () => {
  worker.use(...boxApi([makeBox({ id: 4, locationId: 3 })]).handlers, ...personApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/boxes/4',
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('Not currently in a bay.')).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Place in bay' }))
    .not.toBeInTheDocument();
});

test('a loader places a box in a bay and then vacates it', async () => {
  const location = makeLocation({ id: 3, name: 'Coventry Depot' });
  const bay = makeBay({ id: 9, locationId: 3, code: 'A1' });
  worker.use(
    ...boxApi([makeBox({ id: 4, locationId: 3 })]).handlers,
    ...meApi(),
    ...locationApi([location], [bay]).handlers,
  );

  const screen = await renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await screen.getByLabelText('Bay').selectOptions('9');
  await screen.getByRole('button', { name: 'Place in bay' }).click();

  await expect.element(screen.getByText(/Currently in bay.*A1/)).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Vacate bay' }).click();

  await expect.element(screen.getByText('Not currently in a bay.')).toBeInTheDocument();
});

test('the validate panel says who the box will be signed as', async () => {
  worker.use(...boxApi([makeBox({ id: 4 })]).handlers, ...meApi({ displayName: 'Val Checker' }));

  const screen = await renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await expect.element(screen.getByText(/You will sign as/)).toBeInTheDocument();
  await expect.element(screen.getByText('Val Checker')).toBeInTheDocument();
  await expect.element(screen.getByLabelText('Checked by')).not.toBeInTheDocument();
});

test('a Loader whose login is not linked cannot validate or place a box', async () => {
  worker.use(
    ...boxApi([makeBox({ id: 4, locationId: 3 })]).handlers,
    ...meApi({ personId: null, displayName: null }),
    ...locationApi([makeLocation({ id: 3 })], [makeBay({ id: 9, locationId: 3 })]).handlers,
  );

  const screen = await renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await expect
    .element(screen.getByText(/Your login is not linked to a volunteer/).first())
    .toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Validate box' })).toBeDisabled();
  await expect.element(screen.getByRole('button', { name: 'Place in bay' })).toBeDisabled();
});

test('a box says who last changed it and when', async () => {
  worker.use(
    ...boxApi([
      makeBox({
        id: 4,
        lastChangedByName: 'Olena Shevchenko',
        lastChangedAt: '2026-10-03T18:04:11',
      }),
    ]).handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/boxes/4',
    roles: ['Loader'],
  });

  await expect
    .element(screen.getByText('Last changed by Olena Shevchenko on 2026-10-03 18:04 UTC'))
    .toBeInTheDocument();
});

test('replacing an attested box opens its unattested replacement, which points back', async () => {
  const api = boxApi([makeBox({ id: 4, validated: true, weightKg: 18 })]);
  worker.use(...api.handlers, ...meApi());

  const screen = await renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await screen.getByRole('button', { name: 'Replace box' }).click();

  await expect.element(screen.getByRole('heading', { name: 'Box #501' })).toBeInTheDocument();
  await expect.element(screen.getByText('Open', { exact: true })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Box #4' })).toBeInTheDocument();
  await expect.element(screen.getByText('Replaces', { exact: false })).toBeInTheDocument();
});

test('a box nobody has attested is edited, so it offers no Replace', async () => {
  worker.use(...boxApi([makeBox({ id: 4 })]).handlers, ...meApi());

  const screen = await renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await expect.element(screen.getByRole('heading', { name: 'Box #4' })).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Replace box' })).not.toBeInTheDocument();
});

test('an attested box cannot be deleted, so Delete is not offered', async () => {
  worker.use(...boxApi([makeBox({ id: 4, validated: true, weightKg: 18 })]).handlers, ...meApi());

  const screen = await renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await expect.element(screen.getByRole('heading', { name: 'Box #4' })).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Delete' })).not.toBeInTheDocument();
});

test('a voided box says so, links to its replacement and offers no Replace', async () => {
  worker.use(
    ...boxApi([
      makeBox({
        id: 4,
        validated: true,
        weightKg: 18,
        voided: true,
        voidedAt: '2026-05-02T09:00:00',
        replacedByBoxId: 9,
      }),
    ]).handlers,
    ...meApi(),
  );

  const screen = await renderWithProviders(null, { routes, route: '/boxes/4', roles: ['Loader'] });

  await expect.element(screen.getByText('Voided', { exact: true })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Box #9' })).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Replace box' })).not.toBeInTheDocument();
});

test('a Purchaser may read an attested box but cannot replace it', async () => {
  worker.use(...boxApi([makeBox({ id: 4, validated: true, weightKg: 18 })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/boxes/4',
    roles: ['Purchaser'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Box #4' })).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Replace box' })).not.toBeInTheDocument();
});
