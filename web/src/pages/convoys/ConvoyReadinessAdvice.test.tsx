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
        requirements: [
          {
            code: 'advisory.budget',
            scope: 'Convoy',
            vin: null,
            state: 'Warning',
            ownerRole: 'Dispatcher',
            severity: 'Advisory',
            message: 'No budget set; Fuel is over budget',
            resolveLink: '/convoys/7/budget',
          },
        ],
      }),
    ),
  );

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} />, {
    roles: ['Dispatcher'],
  });

  await expect
    .element(screen.getByRole('heading', { name: 'Ready to travel' }))
    .toBeInTheDocument();
  await expect.element(screen.getByText('No budget set; Fuel is over budget')).toBeInTheDocument();
});
