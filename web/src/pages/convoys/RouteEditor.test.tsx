import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeConvoy, makeRouteStop } from '../../test/factories/convoy';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { RouteEditor } from './RouteEditor';

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('adds a stop, requires a postcode, then saves the whole route', async () => {
  const api = convoyApi([makeConvoy({ id: 3 })]);
  worker.use(...api.handlers);

  const screen = renderWithProviders(<RouteEditor convoyId={3} disabled={false} />, {
    roles: ['Dispatcher'],
  });

  await screen.getByRole('button', { name: 'Add stop' }).click();
  await screen.getByRole('button', { name: 'Save route' }).click();
  await expect.element(screen.getByText('Postcode is required')).toBeInTheDocument();

  await screen.getByLabelText('Postcode').fill('M1 1AA');
  await screen.getByRole('button', { name: 'Save route' }).click();

  await expect.poll(() => api.routes.get(3)?.length).toBe(1);
  expect(api.routes.get(3)?.[0]?.postcode).toBe('M1 1AA');
});

test('is read-only once the truck list is published', async () => {
  const api = convoyApi([makeConvoy({ id: 3, truckListPublished: true })]);
  api.routes.set(3, [makeRouteStop({ sequence: 1, postcode: 'SW1A 1AA' })]);
  worker.use(...api.handlers);

  const screen = renderWithProviders(<RouteEditor convoyId={3} disabled />, {
    roles: ['Dispatcher'],
  });

  await expect
    .element(screen.getByText('The truck list is published — the route is now fixed.'))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Add stop' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Save route' })).not.toBeInTheDocument();
});
