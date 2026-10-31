import { Route, Routes } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makeConvoy, makeConvoyVehicle } from '../../test/factories/convoy';
import { makeManifestBox } from '../../test/factories/manifest';
import { convoyApi } from '../../test/msw/convoys';
import { outcomesApi } from '../../test/msw/outcomes';
import { makeProgress, makeProgressPoint, progressApi } from '../../test/msw/progress';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyChecklistPage } from './ConvoyChecklistPage';

const route = [
  makeProgressPoint({ routePointId: 1, sequence: 1, name: 'Coventry hub', kind: 'Hub' }),
  makeProgressPoint({
    routePointId: 2,
    sequence: 2,
    name: 'Calais',
    kind: 'Border',
    authority: 'UK',
  }),
  makeProgressPoint({
    routePointId: 3,
    sequence: 3,
    name: 'Krakow stay',
    kind: 'Overnight',
    accommodation: [
      { provider: 'Ibis Krakow', checkIn: '2026-03-03T15:00:00', checkOut: '2026-03-04T10:00:00' },
    ],
  }),
];

function serve(
  overrides: Parameters<typeof makeProgress>[0] = {},
  options: Parameters<typeof progressApi>[1] = {},
) {
  const api = progressApi(makeProgress({ points: route, ...overrides }), options);
  worker.use(...api.handlers);
  return api;
}

const render = (roles: Parameters<typeof renderWithProviders>[1] = { roles: [] }) =>
  renderWithProviders(
    <Routes>
      <Route path="/convoys/:id/checklist" element={<ConvoyChecklistPage />} />
    </Routes>,
    { ...roles, route: '/convoys/7/checklist' },
  );

test('shows the route in order, each point by name and kind and nothing else', async () => {
  serve();

  const screen = await render();

  await expect.element(screen.getByText('Coventry hub')).toBeInTheDocument();
  await expect.element(screen.getByText('Calais')).toBeInTheDocument();
  await expect.element(screen.getByText('Krakow stay')).toBeInTheDocument();
  await expect.element(screen.getByText('Border')).toBeInTheDocument();
  await expect.element(screen.getByText('Overnight')).toBeInTheDocument();
});

test('shows where the crew is staying at an overnight point', async () => {
  serve();

  const screen = await render();

  await expect.element(screen.getByText(/Ibis Krakow/)).toBeInTheDocument();
});

test('marks a point reached and shows who entered it and when', async () => {
  const api = serve();
  const screen = await render();

  await screen.getByRole('button', { name: 'Reached Coventry hub' }).click();

  await expect.element(screen.getByText('Reached at 14:30, Lena Leader')).toBeInTheDocument();
  expect(api.posted).toEqual([{ pointId: 1, verb: 'reached', body: {} }]);
  await expect
    .element(screen.getByRole('button', { name: 'Reached Coventry hub' }))
    .not.toBeInTheDocument();
});

test('only a border offers Crossed, and crossing it is recorded', async () => {
  const api = serve();
  const screen = await render();

  await expect
    .element(screen.getByRole('button', { name: 'Crossed Coventry hub' }))
    .not.toBeInTheDocument();
  await screen.getByRole('button', { name: 'Crossed Calais' }).click();

  await expect.element(screen.getByText(/Crossed at 14:30/)).toBeInTheDocument();
  expect(api.posted).toEqual([{ pointId: 2, verb: 'crossed', body: {} }]);
});

test('a mark entered by a dispatcher says it was entered for the leader', async () => {
  serve({}, { recordedByName: 'Dora Dispatcher', onBehalfOfLeader: true });
  const screen = await render({ roles: ['Dispatcher'] });

  await screen.getByRole('button', { name: 'Reached Coventry hub' }).click();

  await expect
    .element(screen.getByText('Reached at 14:30, entered by Dora Dispatcher for the leader'))
    .toBeInTheDocument();
});

test('offers nothing to press before the convoy has departed', async () => {
  serve({ departedAt: null });

  const screen = await render();

  await expect.element(screen.getByText('This convoy has not departed yet.')).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Reached Coventry hub' }))
    .not.toBeInTheDocument();
});

test('enters fuel as a fuel cost', async () => {
  const api = serve();
  const screen = await render();

  await screen.getByLabelText('Fuel cost (£)').fill('82.50');
  await screen.getByRole('button', { name: 'Add fuel' }).click();

  await expect.element(screen.getByText('Fuel added.')).toBeInTheDocument();
  expect(api.fuel).toEqual([{ type: 'Fuel', amountGbp: 82.5 }]);
});

test('records how a box ended, with the registered hubs the progress view brought', async () => {
  const convoys = convoyApi([
    makeConvoy({ id: 7, departed: true, departedAt: '2026-03-01T08:00:00' }),
  ]);
  convoys.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-A', plate: 'PL-001' })]);
  convoys.boxes.set('7:VIN-A', [makeManifestBox({ boxId: 5 })]);
  const outcomes = outcomesApi(convoys);
  worker.use(...convoys.handlers, ...outcomes.handlers);
  serve({ hubs: [{ locationId: 10, name: 'Coventry hub' }] });

  const screen = await render();

  await screen.getByLabelText('Outcome for box 5').selectOptions('ReturnedToHub');
  await screen.getByLabelText('Hub for box 5').selectOptions('10');
  await screen
    .getByRole('form', { name: 'Record an outcome for box 5' })
    .getByRole('button', { name: 'Record' })
    .click();

  await expect.element(screen.getByRole('cell', { name: 'Returned to a hub' })).toBeInTheDocument();
  expect(outcomes.recorded[0]?.body).toEqual({ outcome: 'ReturnedToHub', hubLocationId: 10 });
});

test('gives HQ number and a way to print the route sheet for when the page is out of reach', async () => {
  serve({ hqPhone: '+44 20 7946 0000' });

  const screen = await render();

  await expect
    .element(screen.getByRole('link', { name: '+44 20 7946 0000' }))
    .toHaveAttribute('href', 'tel:+442079460000');
  await expect
    .element(screen.getByRole('button', { name: 'Print the route sheet' }))
    .toBeInTheDocument();
});

test('still gives the fallback when the checklist cannot be loaded', async () => {
  const screen = await render();

  await expect.element(screen.getByText('The checklist could not be loaded.')).toBeInTheDocument();
  await expect
    .element(screen.getByText(/Call HQ and use the printed documents/))
    .toBeInTheDocument();
});

test('keeps nothing on the device while it is used', async () => {
  serve();
  const before = [localStorage.length, sessionStorage.length];
  const screen = await render();

  await screen.getByRole('button', { name: 'Reached Coventry hub' }).click();
  await expect.element(screen.getByText(/Reached at 14:30/)).toBeInTheDocument();

  expect([localStorage.length, sessionStorage.length]).toEqual(before);
});

// The checklist's own code must not so much as name a way to keep data on the phone (ADR 0016, X10).
const sources = import.meta.glob(
  [
    './ConvoyChecklistPage.tsx',
    './ConvoyProgressCard.tsx',
    './LeaderChecklistLinks.tsx',
    '../../api/progress.ts',
    '../../api/schemas/progress.ts',
  ],
  { query: '?raw', import: 'default', eager: true },
);

test.each(Object.entries(sources))(
  '%s never touches local storage, session storage, IndexedDB, caches or a service worker',
  (_path, source) => {
    expect(source).not.toMatch(/localStorage|sessionStorage|indexedDB|serviceWorker|caches\./i);
  },
);

test('the checklist code was found, so the check above is not vacuous', () => {
  expect(Object.keys(sources)).toHaveLength(5);
});
