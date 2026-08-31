import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeReceiver, makeReceiverDetail } from '../../test/factories/receiver';
import { receiverApi } from '../../test/msw/receivers';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ReceiverSensitivePanel } from './ReceiverSensitivePanel';

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('the reveal modal carries the audit warning and blocks an empty reason', async () => {
  const api = receiverApi(
    [makeReceiver({ ref: 'r1' })],
    [makeReceiverDetail({ ref: 'r1', addressLine1: '17 Khreshchatyk', city: 'Kyiv' })],
  );
  worker.use(...api.handlers);

  const screen = renderWithProviders(
    <ReceiverSensitivePanel receiverRef="r1" onDeleted={() => undefined} />,
    { roles: ['GroundOfficer'] },
  );

  await screen.getByRole('button', { name: 'Reveal delivery detail' }).click();
  await expect
    .element(screen.getByText('This access is recorded in the receiver access log.'))
    .toBeInTheDocument();

  await screen.getByRole('button', { name: 'Reveal detail' }).click();
  await expect.element(screen.getByText('State why you need to see this.')).toBeInTheDocument();
  expect(api.accessLog).toHaveLength(0);
});

test('a stated reason reveals the detail and is recorded against the access log', async () => {
  const api = receiverApi(
    [makeReceiver({ ref: 'r1' })],
    [makeReceiverDetail({ ref: 'r1', addressLine1: '17 Khreshchatyk', city: 'Kyiv' })],
  );
  worker.use(...api.handlers);

  const screen = renderWithProviders(
    <ReceiverSensitivePanel receiverRef="r1" onDeleted={() => undefined} />,
    { roles: ['GroundOfficer'] },
  );

  await screen.getByRole('button', { name: 'Reveal delivery detail' }).click();
  await screen.getByLabelText('Reason for viewing').fill('Confirming the drop point for convoy 12');
  await screen.getByRole('button', { name: 'Reveal detail' }).click();

  await expect.element(screen.getByText('17 Khreshchatyk', { exact: false })).toBeInTheDocument();
  await expect.poll(() => api.accessLog.length).toBe(1);
  expect(api.accessLog[0]?.reason).toBe('Confirming the drop point for convoy 12');
});

test('when no detail is recorded, the panel offers to add it', async () => {
  const api = receiverApi([makeReceiver({ ref: 'r1' })]);
  worker.use(...api.handlers);

  const screen = renderWithProviders(
    <ReceiverSensitivePanel receiverRef="r1" onDeleted={() => undefined} />,
    { roles: ['GroundOfficer'] },
  );

  await screen.getByRole('button', { name: 'Reveal delivery detail' }).click();
  await screen.getByLabelText('Reason for viewing').fill('Setting up a new delivery point');
  await screen.getByRole('button', { name: 'Reveal detail' }).click();

  await expect
    .element(screen.getByText('No delivery detail has been recorded for this receiver.'))
    .toBeInTheDocument();

  await screen.getByRole('button', { name: 'Add delivery detail' }).click();
  await screen.getByLabelText('Contact name').fill('Iryna S');
  await screen.getByLabelText('Contact phone').fill('+380 44 000 0000');
  await screen.getByLabelText('Address line 1').fill('12 Main St');
  await screen.getByLabelText('City').fill('Lviv');
  await screen.getByRole('button', { name: 'Save delivery detail' }).click();

  await expect.element(screen.getByText('12 Main St', { exact: false })).toBeInTheDocument();
  expect(api.details.get('r1')?.contactName).toBe('Iryna S');
});

test('delete receiver invokes the onDeleted callback', async () => {
  const api = receiverApi([makeReceiver({ ref: 'r1' })]);
  worker.use(...api.handlers);
  let deleted = false;

  const screen = renderWithProviders(
    <ReceiverSensitivePanel
      receiverRef="r1"
      onDeleted={() => {
        deleted = true;
      }}
    />,
    { roles: ['GroundOfficer'] },
  );

  await screen.getByRole('button', { name: 'Delete receiver' }).click();

  await expect.poll(() => deleted).toBe(true);
  expect(api.db.has('r1')).toBe(false);
});
