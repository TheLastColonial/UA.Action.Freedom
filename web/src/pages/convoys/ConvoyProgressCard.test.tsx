import { expect, test } from 'vitest';

import { meApi } from '../../test/msw/me';
import { makeProgress, makeProgressPoint, progressApi } from '../../test/msw/progress';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { Dashboard } from '../Dashboard';
import { ConvoyProgressCard } from './ConvoyProgressCard';

const marked = makeProgressPoint({
  routePointId: 2,
  sequence: 2,
  name: 'Calais',
  kind: 'Border',
  authority: 'UK',
  marks: [
    {
      routePointId: 2,
      kind: 'Crossed',
      vin: null,
      recordedByName: 'Dora Dispatcher',
      recordedAt: '2026-03-02T14:30:00',
      onBehalfOfLeader: true,
    },
  ],
});

test("HQ's view says what has been reported, and who entered it for the leader", async () => {
  worker.use(
    ...progressApi(
      makeProgress({
        points: [makeProgressPoint({ routePointId: 1, name: 'Coventry hub', kind: 'Hub' }), marked],
      }),
    ).handlers,
  );

  const screen = await renderWithProviders(<ConvoyProgressCard convoyId={7} departed />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('Coventry hub (Hub)')).toBeInTheDocument();
  await expect.element(screen.getByText('Not yet')).toBeInTheDocument();
  await expect
    .element(screen.getByText('Crossed 2026-03-02 14:30 (Dora Dispatcher, for the leader)'))
    .toBeInTheDocument();
});

test('shows nothing before the convoy has departed', async () => {
  worker.use(...progressApi(makeProgress({ points: [marked] })).handlers);

  const screen = await renderWithProviders(<ConvoyProgressCard convoyId={7} departed={false} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('Calais (Border)')).not.toBeInTheDocument();
});

test('a convoy leader, who has no role in their token, finds their checklist from the dashboard', async () => {
  worker.use(...meApi({ ledConvoyIds: [7] }));

  const screen = await renderWithProviders(<Dashboard />, { roles: [] });

  await expect
    .element(screen.getByRole('link', { name: 'Checklist for convoy 7' }))
    .toHaveAttribute('href', '/convoys/7/checklist');
});

test('someone who leads no convoy is offered no checklist', async () => {
  worker.use(...meApi({ ledConvoyIds: [] }));

  const screen = await renderWithProviders(<Dashboard />, { roles: ['Administrator'] });

  await expect.element(screen.getByRole('link', { name: 'Convoys' })).toBeInTheDocument();
  await expect.element(screen.getByText(/Checklist for convoy/)).not.toBeInTheDocument();
});
