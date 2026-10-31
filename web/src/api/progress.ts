import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';

import { getJson, post204 } from './http';
import { qk } from './queryKeys';
import { convoyProgressSchema } from './schemas/progress';
import type { ConvoyProgress, RoutePointMarkKind } from './schemas/progress';

const convoyPath = (id: number) => `/convoys/${String(id)}`;

export function fetchProgress(id: number): Promise<ConvoyProgress> {
  return getJson(`${convoyPath(id)}/progress`, convoyProgressSchema);
}

// The Convoy Leader's act; a Dispatcher may do it on their behalf and the record names them (O27).
export function markPoint(
  id: number,
  pointId: number,
  kind: RoutePointMarkKind,
  vin?: string,
): Promise<void> {
  const verb = kind === 'Reached' ? 'reached' : 'crossed';
  return post204(`${convoyPath(id)}/route/${String(pointId)}/${verb}`, vin ? { vin } : {});
}

/**
 * What the checklist and HQ's progress view read. Nothing here is kept: the answer is not cached beyond the screen
 * showing it (ADR 0016, X10), so a closed page leaves nothing behind in the query cache either.
 */
export function useConvoyProgress(id: number): UseQueryResult<ConvoyProgress> {
  return useQuery({
    queryKey: qk.convoys.progress(id),
    queryFn: () => fetchProgress(id),
    gcTime: 0,
    staleTime: 0,
  });
}

export function useMarkPoint(
  id: number,
): UseMutationResult<void, Error, { pointId: number; kind: RoutePointMarkKind; vin?: string }> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ pointId, kind, vin }) => markPoint(id, pointId, kind, vin),
    // A crossing closes declarations, so what the declarations screens show has changed too.
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.convoys.all });
      await queryClient.invalidateQueries({ queryKey: qk.declarations.convoy(id) });
    },
  });
}
