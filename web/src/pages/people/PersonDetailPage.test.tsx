import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makePerson } from '../../test/factories/person';
import { personApi } from '../../test/msw/people';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { peopleRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'people', children: peopleRoutes },
];

test('renders the volunteer', async () => {
  worker.use(
    ...personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K', isDriver: true })])
      .handlers,
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/people/p1',
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Olena K' })).toBeInTheDocument();
});

test('groups fields into named cards', async () => {
  worker.use(...personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/people/p1',
    roles: ['Loader'],
  });

  for (const name of ['Personal details', 'Volunteering']) {
    await expect.element(screen.getByRole('region', { name })).toBeInTheDocument();
  }
});

test('renders Not found for an unknown id', async () => {
  worker.use(...personApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/people/gone',
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('hides Edit and Delete from a non-administrator', async () => {
  worker.use(...personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/people/p1',
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Olena K' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Erase volunteer' }))
    .not.toBeInTheDocument();
});

test('an administrator erases a volunteer after confirming, and returns to the list', async () => {
  const api = personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' })]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/people/p1',
    roles: ['Administrator'],
  });

  await screen.getByRole('button', { name: 'Erase volunteer' }).click();
  await expect
    .element(screen.getByRole('alertdialog'))
    .toHaveTextContent("permanently erases Olena K's personal details");
  expect(api.db.has('p1')).toBe(true);

  await screen.getByRole('button', { name: 'Erase permanently' }).click();

  await expect.element(screen.getByRole('heading', { name: 'Volunteers' })).toBeInTheDocument();
  expect(api.db.has('p1')).toBe(false);
});

test('cancelling the confirmation keeps the volunteer', async () => {
  const api = personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' })]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/people/p1',
    roles: ['Administrator'],
  });

  await screen.getByRole('button', { name: 'Erase volunteer' }).click();
  await screen.getByRole('button', { name: 'Cancel' }).click();

  await expect.element(screen.getByRole('alertdialog')).not.toBeInTheDocument();
  expect(api.db.has('p1')).toBe(true);
});

test('a volunteer still on a live crew is not erased, and the reason is shown', async () => {
  const api = personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' })], {
    activeIds: ['p1'],
  });
  worker.use(...api.handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/people/p1',
    roles: ['Administrator'],
  });

  await screen.getByRole('button', { name: 'Erase volunteer' }).click();
  await screen.getByRole('button', { name: 'Erase permanently' }).click();

  await expect.element(screen.getByRole('alert')).toHaveTextContent('Take them off it');
  expect(api.db.has('p1')).toBe(true);
});
