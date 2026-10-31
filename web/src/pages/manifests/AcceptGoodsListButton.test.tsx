import { HttpResponse, http } from 'msw';
import { expect, test } from 'vitest';

import { worker } from '../../test/msw/worker';
import { problem } from '../../test/msw/problem';
import { renderWithProviders } from '../../test/render';
import { AcceptGoodsListButton } from './AcceptGoodsListButton';

const HOSPITAL = 'bbbbbbbb-0000-4000-8000-000000000001';

test('accepts the goods list for the receiver and says so', async () => {
  const accepted: string[] = [];
  worker.use(
    http.post(
      '/convoys/7/vehicles/VIN-A/declarations/goods-list/:receiverRef/accepted',
      ({ params }) => {
        accepted.push(String(params['receiverRef']));
        return HttpResponse.json({ boxesAccepted: 2 });
      },
    ),
  );
  const screen = await renderWithProviders(
    <AcceptGoodsListButton convoyId={7} vin="VIN-A" receiverRef={HOSPITAL} />,
    { roles: ['Dispatcher'] },
  );

  await screen.getByRole('button', { name: 'Accept goods list' }).click();

  await expect.element(screen.getByRole('status')).toHaveTextContent('Accepted.');
  expect(accepted).toEqual([HOSPITAL]);
});

test('shows why nothing could be accepted', async () => {
  worker.use(
    http.post('/convoys/7/vehicles/VIN-A/declarations/goods-list/:receiverRef/accepted', () =>
      problem(409, 'No box for that Receiver is waiting to be accepted on this vehicle.', {
        type: 'goods-list-nothing-to-accept',
      }),
    ),
  );
  const screen = await renderWithProviders(
    <AcceptGoodsListButton convoyId={7} vin="VIN-A" receiverRef={HOSPITAL} />,
    { roles: ['Dispatcher'] },
  );

  await screen.getByRole('button', { name: 'Accept goods list' }).click();

  await expect
    .element(screen.getByRole('alert'))
    .toHaveTextContent('No box for that Receiver is waiting');
});

test('is not offered to a Loader', async () => {
  const screen = await renderWithProviders(
    <AcceptGoodsListButton convoyId={7} vin="VIN-A" receiverRef={HOSPITAL} />,
    { roles: ['Loader'] },
  );

  expect(screen.getByRole('button', { name: 'Accept goods list' }).elements()).toHaveLength(0);
});
