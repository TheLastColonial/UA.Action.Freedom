import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makeConvoy } from '../../test/factories/convoy';
import { makeConvoyReport } from '../../test/factories/outcomes';
import { convoyApi } from '../../test/msw/convoys';
import { outcomesApi } from '../../test/msw/outcomes';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { convoyRoutes } from './routes';

const routes: RouteObject[] = [{ path: 'convoys', children: convoyRoutes }];

function serve(report = makeConvoyReport()) {
  const convoys = convoyApi([
    makeConvoy({ id: 7, arrived: true, arrivedAt: '2026-03-06T20:00:00' }),
  ]);
  const outcomes = outcomesApi(convoys);
  outcomes.reports.set(7, report);
  worker.use(...convoys.handlers, ...outcomes.handlers);
}

const render = () =>
  renderWithProviders(null, { routes, route: '/convoys/7/report', roles: ['Dispatcher'] });

test('shows expected against actual costs, and over-budget lines', async () => {
  serve();

  const screen = await render();

  const costs = screen.getByRole('region', { name: 'Costs' });
  await expect.element(costs.getByRole('cell', { name: 'Fuel' })).toBeInTheDocument();
  await expect.element(costs.getByRole('cell', { name: 'Over budget' })).toBeInTheDocument();
  await expect
    .element(costs.getByText(/Expected £1,200\.00; actual £1,350\.00\./))
    .toBeInTheDocument();
});

test('counts delivered value without the seized or returned boxes', async () => {
  serve();

  const screen = await render();

  const totals = screen.getByRole('region', { name: 'Totals' });
  await expect.element(totals.getByText('Value delivered')).toBeInTheDocument();
  await expect.element(totals.getByText('£100.00')).toBeInTheDocument();
  await expect.element(totals.getByText('Boxes not delivered')).toBeInTheDocument();
});

test('names receivers at organisation and region only', async () => {
  serve();

  const screen = await render();

  const receivers = screen.getByRole('region', { name: 'Receivers' });
  await expect
    .element(receivers.getByText(/Lviv Hospital 4, Lviv — 1 of 2 boxes delivered/))
    .toBeInTheDocument();
  await expect
    .element(receivers.getByText(/Kyiv Clinic, Kyiv — 0 of 1 boxes delivered/))
    .toBeInTheDocument();
});

test('says so when the convoy has not been closed', async () => {
  serve(makeConvoyReport({ closedAt: null }));

  const screen = await render();

  await expect.element(screen.getByText(/Not closed yet\./)).toBeInTheDocument();
});

test('says when no budget or costs were recorded', async () => {
  serve(makeConvoyReport({ budget: null }));

  const screen = await render();

  await expect.element(screen.getByText('No budget or costs were recorded.')).toBeInTheDocument();
});

test('offers a way back to the convoy and a print button', async () => {
  serve();

  const screen = await render();

  await expect.element(screen.getByRole('link', { name: 'Back to convoy' })).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Print' })).toBeInTheDocument();
});

test('Not found for a convoy that has no report', async () => {
  serve();

  const screen = await renderWithProviders(null, {
    routes,
    route: '/convoys/999/report',
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});
