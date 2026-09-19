import { render } from 'vitest-browser-react';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';

import { ErrorBoundary } from './ErrorBoundary';

function Explodes(): never {
  throw new Error('render failed');
}

beforeEach(() => {
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});
afterEach(() => {
  vi.restoreAllMocks();
});

test('renders its children when nothing goes wrong', async () => {
  const screen = await render(
    <ErrorBoundary>
      <p>All well</p>
    </ErrorBoundary>,
  );

  await expect.element(screen.getByText('All well')).toBeInTheDocument();
});

test('replaces a crashed page with a way to recover', async () => {
  const screen = await render(
    <ErrorBoundary>
      <Explodes />
    </ErrorBoundary>,
  );

  await expect.element(screen.getByRole('alert')).toHaveTextContent('Something went wrong');
  await expect.element(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
});
