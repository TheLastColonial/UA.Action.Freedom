import { expect, test } from 'vitest';

import { makeConvoy, makeRouteStop } from '../../test/factories/convoy';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { RouteEditor } from './RouteEditor';

test('adds a stop, requires a postcode, then saves the whole route', async () => {
  const api = convoyApi([makeConvoy({ id: 3 })]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(<RouteEditor convoyId={3} disabled={false} />, {
    roles: ['Dispatcher'],
  });

  await screen.getByRole('button', { name: 'Add stop' }).click();
  await screen.getByRole('button', { name: 'Save route' }).click();
  await expect.element(screen.getByText('Postcode is required')).toBeInTheDocument();

  await screen.getByLabelText('Postcode').fill('M1 1AA');
  await screen.getByLabelText('Name').fill('Manchester depot');
  await screen.getByRole('button', { name: 'Save route' }).click();

  await expect.poll(() => api.routes.get(3)?.length).toBe(1);
  expect(api.routes.get(3)?.[0]?.postcode).toBe('M1 1AA');
});

test('a border point needs the authority it is a crossing for', async () => {
  const api = convoyApi([makeConvoy({ id: 3 })]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(<RouteEditor convoyId={3} disabled={false} />, {
    roles: ['Dispatcher'],
  });

  await screen.getByRole('button', { name: 'Add stop' }).click();
  await screen.getByLabelText('Name').fill('Dover');
  await screen.getByLabelText('Postcode').fill('CT16 1JA');
  await screen.getByLabelText('Kind').selectOptions('Border');
  await screen.getByRole('button', { name: 'Save route' }).click();
  await expect
    .element(screen.getByText('A border point needs the authority it is a crossing for'))
    .toBeInTheDocument();

  await screen.getByLabelText('Customs authority').selectOptions('UK');
  await screen.getByRole('button', { name: 'Save route' }).click();

  await expect.poll(() => api.routes.get(3)?.[0]?.authority).toBe('UK');
  expect(api.routes.get(3)?.[0]?.kind).toBe('Border');
});

test('keeps a point id through a reorder and an edit', async () => {
  const api = convoyApi([makeConvoy({ id: 3 })]);
  api.routes.set(3, [
    makeRouteStop({ sequence: 1, routePointId: 11, name: 'Coventry', postcode: 'CV1 2AB' }),
    makeRouteStop({ sequence: 2, routePointId: 12, name: 'Dover', postcode: 'CT16 1JA' }),
  ]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(<RouteEditor convoyId={3} disabled={false} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('Stop 2')).toBeInTheDocument();
  await screen.getByRole('button', { name: 'Move up' }).nth(1).click();
  await screen.getByRole('button', { name: 'Save route' }).click();

  await expect
    .poll(() => api.routes.get(3)?.map((stop) => stop.name))
    .toEqual(['Dover', 'Coventry']);
  expect(api.routes.get(3)?.map((stop) => stop.routePointId)).toEqual([12, 11]);
});

test('is read-only once the truck list is published', async () => {
  const api = convoyApi([makeConvoy({ id: 3, truckListPublished: true })]);
  api.routes.set(3, [makeRouteStop({ sequence: 1, postcode: 'SW1A 1AA' })]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(<RouteEditor convoyId={3} disabled />, {
    roles: ['Dispatcher'],
  });

  await expect
    .element(screen.getByText('The truck list is published — the route is now fixed.'))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Add stop' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Save route' })).not.toBeInTheDocument();
});
