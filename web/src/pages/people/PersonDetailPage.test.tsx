import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makePerson } from '../../test/factories/person';
import { meApi } from '../../test/msw/me';
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
  worker.use(...api.handlers, ...meApi());

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
  worker.use(...api.handlers, ...meApi());

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
  worker.use(...api.handlers, ...meApi());

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

test('an administrator links a login to a volunteer', async () => {
  const api = personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' })]);
  worker.use(...api.handlers, ...meApi({ subject: 'kc-admin' }));

  const screen = await renderWithProviders(null, {
    routes,
    route: '/people/p1',
    roles: ['Administrator'],
  });

  await screen.getByLabelText('Login subject').fill('kc-olena');
  await screen.getByRole('button', { name: 'Link login' }).click();

  await expect.element(screen.getByText('Login linked.')).toBeInTheDocument();
  expect(api.logins.get('kc-olena')).toBe('p1');
});

test('Use my login fills in the administrator own subject, so the first link can be made', async () => {
  worker.use(
    ...personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' })]).handlers,
    ...meApi({ subject: 'kc-admin' }),
  );

  const screen = await renderWithProviders(null, {
    routes,
    route: '/people/p1',
    roles: ['Administrator'],
  });

  await screen.getByRole('button', { name: 'Use my login' }).click();

  await expect.element(screen.getByLabelText('Login subject')).toHaveValue('kc-admin');
});

test('linking a login that belongs to someone else shows the conflict', async () => {
  const api = personApi([
    makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' }),
    makePerson({ id: 'p2', firstName: 'Ivan', lastName: 'M' }),
  ]);
  api.logins.set('kc-ivan', 'p2');
  worker.use(...api.handlers, ...meApi());

  const screen = await renderWithProviders(null, {
    routes,
    route: '/people/p1',
    roles: ['Administrator'],
  });

  await screen.getByLabelText('Login subject').fill('kc-ivan');
  await screen.getByRole('button', { name: 'Link login' }).click();

  await expect
    .element(screen.getByText('That login is already linked to another volunteer.'))
    .toBeInTheDocument();
});

test('hides the link-login panel from a non-administrator', async () => {
  worker.use(...personApi([makePerson({ id: 'p1', firstName: 'Olena', lastName: 'K' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/people/p1',
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Olena K' })).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Link login' })).not.toBeInTheDocument();
});
