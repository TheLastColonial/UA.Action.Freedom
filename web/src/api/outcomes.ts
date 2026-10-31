import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import { getJson, postJson } from './http';
import { qk } from './queryKeys';
import {
  acceptedGoodsListSchema,
  boxOutcomeReadModelSchema,
  convoyReportSchema,
  deliveredVehicleSchema,
  recordedBoxOutcomeSchema,
} from './schemas/outcomes';
import type {
  BoxOutcomeReadModel,
  ConvoyReport,
  RecordBoxOutcomeRequest,
} from './schemas/outcomes';

const convoyPath = (id: number) => `/convoys/${String(id)}`;
const vehiclePath = (id: number, vin: string) =>
  `${convoyPath(id)}/vehicles/${encodeURIComponent(vin)}`;

export function fetchBoxOutcomes(id: number): Promise<readonly BoxOutcomeReadModel[]> {
  return getJson(`${convoyPath(id)}/outcomes`, z.array(boxOutcomeReadModelSchema));
}

// How a box ended on the convoy (ADR 0015). The Dispatcher records it for the Convoy Leader.
export function recordBoxOutcome(id: number, boxId: number, body: RecordBoxOutcomeRequest) {
  return postJson(
    `${convoyPath(id)}/boxes/${String(boxId)}/outcome`,
    body,
    recordedBoxOutcomeSchema,
  );
}

export function deliverVehicle(id: number, vin: string) {
  return postJson(`${vehiclePath(id, vin)}/delivered`, {}, deliveredVehicleSchema);
}

// Acceptance is recorded once per Ukrainian goods list: every delivered box for that Receiver on that vehicle.
export function acceptGoodsList(id: number, vin: string, receiverRef: string) {
  return postJson(
    `${vehiclePath(id, vin)}/declarations/goods-list/${encodeURIComponent(receiverRef)}/accepted`,
    {},
    acceptedGoodsListSchema,
  );
}

// Closing does not lock the convoy (O25): it can be pressed again after a correction.
export function closeConvoy(id: number): Promise<ConvoyReport> {
  return postJson(`${convoyPath(id)}/close`, {}, convoyReportSchema);
}

export function fetchConvoyReport(id: number): Promise<ConvoyReport> {
  return getJson(`${convoyPath(id)}/report`, convoyReportSchema);
}

export function useBoxOutcomes(id: number): UseQueryResult<readonly BoxOutcomeReadModel[]> {
  return useQuery({ queryKey: qk.convoys.outcomes(id), queryFn: () => fetchBoxOutcomes(id) });
}

export function useConvoyReport(id: number): UseQueryResult<ConvoyReport> {
  return useQuery({ queryKey: qk.convoys.report(id), queryFn: () => fetchConvoyReport(id) });
}

// An outcome changes what is on a vehicle's load, so its declarations, the cargo list and the report are all stale.
function useInvalidateOutcomes(id: number) {
  const queryClient = useQueryClient();
  return async () => {
    await queryClient.invalidateQueries({ queryKey: qk.convoys.all });
    await queryClient.invalidateQueries({ queryKey: qk.manifests.all });
    await queryClient.invalidateQueries({ queryKey: qk.declarations.convoy(id) });
  };
}

export function useRecordBoxOutcome(
  id: number,
): UseMutationResult<unknown, Error, { boxId: number; body: RecordBoxOutcomeRequest }> {
  const invalidate = useInvalidateOutcomes(id);
  return useMutation({
    mutationFn: ({ boxId, body }) => recordBoxOutcome(id, boxId, body),
    onSettled: invalidate,
  });
}

export function useDeliverVehicle(id: number): UseMutationResult<unknown, Error, string> {
  const invalidate = useInvalidateOutcomes(id);
  return useMutation({ mutationFn: (vin) => deliverVehicle(id, vin), onSettled: invalidate });
}

export function useAcceptGoodsList(
  id: number,
  vin: string,
): UseMutationResult<unknown, Error, string> {
  const invalidate = useInvalidateOutcomes(id);
  return useMutation({
    mutationFn: (receiverRef) => acceptGoodsList(id, vin, receiverRef),
    onSettled: invalidate,
  });
}

export function useCloseConvoy(id: number): UseMutationResult<ConvoyReport, Error, void> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => closeConvoy(id),
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.convoys.all });
    },
  });
}
