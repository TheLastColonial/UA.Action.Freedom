import '@testing-library/jest-dom/vitest';

import { afterAll, afterEach, beforeAll, beforeEach } from 'vitest';
import { cleanup } from 'vitest-browser-react';

import { resetApiClient } from '../api/client';
import { worker } from './msw/worker';

beforeAll(async () => {
  await worker.start({ onUnhandledRequest: 'bypass', quiet: true });
});

// Every test starts from an unconfigured API client; renderWithProviders configures it.
beforeEach(() => {
  resetApiClient();
});

afterEach(async () => {
  await cleanup();
  worker.resetHandlers();
  resetApiClient();
});

afterAll(() => {
  worker.stop();
});
