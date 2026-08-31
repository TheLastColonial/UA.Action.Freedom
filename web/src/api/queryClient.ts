import { QueryClient } from '@tanstack/react-query';

import { ApiError } from './problem';

// Retry transient failures (a dropped connection, a 5xx while the app or database is waking)
// but never a deliberate answer — a 4xx is an ApiError subclass and must surface at once.
function retry(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError) {
    return false;
  }
  return failureCount < 3;
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        gcTime: 5 * 60_000,
        retry,
        retryDelay: (attempt) => Math.min(1000 * 2 ** attempt, 15_000),
      },
      mutations: {
        retry: false,
      },
    },
  });
}
