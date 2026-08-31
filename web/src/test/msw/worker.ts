import { setupWorker } from 'msw/browser';

// Handlers are added per test with `worker.use(...)`. Slice suites register their default
// handlers through `src/test/msw/handlers`.
export const worker = setupWorker();
