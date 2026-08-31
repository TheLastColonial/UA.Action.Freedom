import { expect, test } from 'vitest';

import { renderWithProviders } from '../test/render';
import { Gate } from './Gate';

test('shows children when the user holds the policy', async () => {
  const screen = renderWithProviders(
    <Gate policy="receivers:detail">
      <span>delivery address</span>
    </Gate>,
    { roles: ['GroundOfficer'] },
  );

  await expect.element(screen.getByText('delivery address')).toBeInTheDocument();
});

test('renders the fallback when the user lacks the policy', async () => {
  const screen = renderWithProviders(
    <Gate policy="receivers:detail" fallback={<span>hidden</span>}>
      <span>delivery address</span>
    </Gate>,
    { roles: ['Dispatcher'] },
  );

  await expect.element(screen.getByText('hidden')).toBeInTheDocument();
  await expect.element(screen.getByText('delivery address')).not.toBeInTheDocument();
});
