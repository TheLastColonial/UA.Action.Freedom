import { http, HttpResponse } from 'msw';
import { expect, test } from 'vitest';

import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyReadinessPanel } from './ConvoyReadinessPanel';

test('budget advice is shown without making the convoy not ready', async () => {
  worker.use(
    http.get('/convoys/7/readiness', () =>
      HttpResponse.json({
        ready: true,
        routePlanned: true,
        reasons: [],
        vehicles: [],
        advisories: ['No budget set', 'Fuel is over budget'],
      }),
    ),
  );

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} />, {
    roles: ['Dispatcher'],
  });

  await expect
    .element(screen.getByRole('heading', { name: 'Ready to travel' }))
    .toBeInTheDocument();
  await expect.element(screen.getByText('No budget set')).toBeInTheDocument();
  await expect.element(screen.getByText('Fuel is over budget')).toBeInTheDocument();
});
