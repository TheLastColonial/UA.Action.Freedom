import '@testing-library/jest-dom/vitest';

import { afterAll, afterEach, beforeAll } from 'vitest';
import { cleanup } from 'vitest-browser-react';

import { worker } from './msw/worker';

beforeAll(async () => {
  await worker.start({ onUnhandledRequest: 'bypass', quiet: true });
});

afterEach(() => {
  cleanup();
  worker.resetHandlers();
});

afterAll(() => {
  worker.stop();
});
