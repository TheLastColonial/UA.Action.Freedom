import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makeCategory } from '../../test/factories/category';
import { categoryApi } from '../../test/msw/categories';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { categoryRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'categories', children: categoryRoutes },
];

const medicine = makeCategory({
  id: 21,
  nameEn: 'Medicine',
  isFixed: true,
  isSensitive: true,
  warnWithinDays: 180,
  euCode: '300490',
});
const gas = makeCategory({
  id: 22,
  nameEn: 'Gas',
  isFixed: true,
  hazardClass: 2,
  isNotCarried: true,
});

test('lists each category with its handling and the code it maps to per authority', async () => {
  worker.use(...categoryApi([medicine, gas]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/categories',
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('Medicine (built in)')).toBeInTheDocument();
  await expect.element(screen.getByText('Sensitive')).toBeInTheDocument();
  await expect.element(screen.getByText('180 days')).toBeInTheDocument();
  await expect.element(screen.getByText('300490')).toBeInTheDocument();
  await expect.element(screen.getByText('Hazard class 2, Not carried')).toBeInTheDocument();
});

test('only an administrator may add or edit a category', async () => {
  worker.use(...categoryApi([medicine]).handlers);

  const asLoader = await renderWithProviders(null, {
    routes,
    route: '/categories',
    roles: ['Loader'],
  });
  await expect.element(asLoader.getByText('Medicine (built in)')).toBeInTheDocument();
  await expect
    .element(asLoader.getByRole('link', { name: 'New category' }))
    .not.toBeInTheDocument();
  await expect
    .element(asLoader.getByRole('link', { name: 'Edit Medicine' }))
    .not.toBeInTheDocument();

  const asAdmin = await renderWithProviders(null, {
    routes,
    route: '/categories',
    roles: ['Administrator'],
  });
  await expect
    .element(asAdmin.getByRole('link', { name: 'New category' }))
    .toHaveAttribute('href', '/categories/new');
  await expect
    .element(asAdmin.getByRole('link', { name: 'Edit Medicine' }))
    .toHaveAttribute('href', '/categories/21/edit');
});

test('creates a category and lands on its page to map the codes', async () => {
  const api = categoryApi([medicine]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/categories/new',
    roles: ['Administrator'],
  });

  await screen.getByLabelText('Name', { exact: true }).fill('Bedding');
  await screen.getByLabelText('Short shelf life within (days)').fill('90');
  await screen.getByRole('button', { name: 'Create category' }).click();

  await expect.element(screen.getByRole('heading', { name: 'Edit Bedding' })).toBeInTheDocument();
  const created = [...api.db.values()].find((c) => c.nameEn === 'Bedding');
  expect(created?.isFixed).toBe(false);
  expect(created?.warnWithinDays).toBe(90);
});

test('refuses a name that is taken and says so', async () => {
  worker.use(...categoryApi([medicine]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/categories/new',
    roles: ['Administrator'],
  });

  await screen.getByLabelText('Name', { exact: true }).fill('medicine');
  await screen.getByRole('button', { name: 'Create category' }).click();

  await expect
    .element(screen.getByText("A category named 'medicine' already exists."))
    .toBeInTheDocument();
});

test('validates the form before it is sent', async () => {
  worker.use(...categoryApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/categories/new',
    roles: ['Administrator'],
  });

  await screen.getByLabelText('Hazard class').fill('12');
  await screen.getByRole('button', { name: 'Create category' }).click();

  await expect.element(screen.getByText('Name is required')).toBeInTheDocument();
  await expect
    .element(screen.getByText('Hazard class must be a whole number from 1 to 9'))
    .toBeInTheDocument();
});

test('edits a category and keeps it built in', async () => {
  const api = categoryApi([medicine]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/categories/21/edit',
    roles: ['Administrator'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Edit Medicine' })).toBeInTheDocument();
  await screen.getByLabelText('Short shelf life within (days)').fill('120');
  await screen.getByRole('button', { name: 'Save changes' }).click();

  await expect
    .element(screen.getByRole('heading', { name: 'Item categories' }))
    .toBeInTheDocument();
  expect(api.db.get(21)?.warnWithinDays).toBe(120);
  expect(api.db.get(21)?.isFixed).toBe(true);
  expect(api.db.get(21)?.euCode).toBe('300490');
});

test('sets a customs code for an authority and clears it again', async () => {
  const api = categoryApi([medicine]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/categories/21/edit',
    roles: ['Administrator'],
  });

  await screen.getByLabelText('United Kingdom (UK) code').fill('30049000');
  await screen.getByRole('button', { name: 'Save UK code' }).click();
  await expect.poll(() => api.db.get(21)?.ukCode).toBe('30049000');

  await screen.getByLabelText('European Union (EU) code').fill('');
  await screen.getByRole('button', { name: 'Save EU code' }).click();
  await expect.poll(() => api.db.get(21)?.euCode).toBeNull();
});

test('rejects a customs code that is not 6 to 10 digits', async () => {
  const api = categoryApi([medicine]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/categories/21/edit',
    roles: ['Administrator'],
  });

  await screen.getByLabelText('Ukraine (UA) code').fill('1234');
  await screen.getByRole('button', { name: 'Save UA code' }).click();

  await expect.element(screen.getByText('A customs code is 6 to 10 digits')).toBeInTheDocument();
  expect(api.db.get(21)?.uaCode).toBeNull();
});

test('a category that does not exist is not found', async () => {
  worker.use(...categoryApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/categories/99/edit',
    roles: ['Administrator'],
  });

  await expect.element(screen.getByText(/not found/i)).toBeInTheDocument();
});
