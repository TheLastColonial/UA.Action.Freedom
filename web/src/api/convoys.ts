import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource, ParentMissing } from './client';
import { delete204, getCollection, getJson, postCreate, postTransition, put204 } from './http';
import { qk } from './queryKeys';
import type { PageParams } from './queryKeys';
import {
  convoyReadModelSchema,
  convoyVehicleReadModelSchema,
  routeStopReadModelSchema,
} from './schemas/convoys';
import type {
  ConvoyReadModel,
  ConvoyVehicleReadModel,
  CreateConvoyRequest,
  ReplaceConvoyRouteRequest,
  RouteStopReadModel,
  UpdateConvoyRequest,
} from './schemas/convoys';

const BASE = '/convoys';
const idPath = (id: number) => `${BASE}/${String(id)}`;
const vinPath = (id: number, vin: string) => `${idPath(id)}/vehicles/${encodeURIComponent(vin)}`;

export function fetchConvoys(params: PageParams): Promise<readonly ConvoyReadModel[]> {
  return getJson(BASE, z.array(convoyReadModelSchema), {
    page: params.page,
    pageSize: params.pageSize,
  });
}

export function fetchConvoy(id: number): Promise<ConvoyReadModel> {
  return getJson(idPath(id), convoyReadModelSchema);
}

export function createConvoy(body: CreateConvoyRequest): Promise<CreatedResource> {
  return postCreate(BASE, body);
}

export function updateConvoy(id: number, body: UpdateConvoyRequest): Promise<void> {
  return put204(idPath(id), body);
}

export function deleteConvoy(id: number): Promise<void> {
  return delete204(idPath(id));
}

export function fetchConvoyRoute(
  id: number,
): Promise<readonly RouteStopReadModel[] | ParentMissing> {
  return getCollection(`${idPath(id)}/route`, routeStopReadModelSchema);
}

export function replaceConvoyRoute(id: number, body: ReplaceConvoyRouteRequest): Promise<void> {
  return put204(`${idPath(id)}/route`, body);
}

export function fetchConvoyVehicles(
  id: number,
): Promise<readonly ConvoyVehicleReadModel[] | ParentMissing> {
  return getCollection(`${idPath(id)}/vehicles`, convoyVehicleReadModelSchema);
}

export function assignVehicle(id: number, vin: string): Promise<void> {
  return put204(vinPath(id, vin));
}

export function unassignVehicle(id: number, vin: string): Promise<void> {
  return delete204(vinPath(id, vin));
}

export function publishTruckList(id: number): Promise<void> {
  return postTransition(`${idPath(id)}/publish-truck-list`);
}

export function useConvoys(params: PageParams): UseQueryResult<readonly ConvoyReadModel[]> {
  return useQuery({ queryKey: qk.convoys.list(params), queryFn: () => fetchConvoys(params) });
}

export function useConvoy(id: number): UseQueryResult<ConvoyReadModel> {
  return useQuery({ queryKey: qk.convoys.detail(id), queryFn: () => fetchConvoy(id) });
}

export function useConvoyRoute(
  id: number,
): UseQueryResult<readonly RouteStopReadModel[] | ParentMissing> {
  return useQuery({ queryKey: qk.convoys.route(id), queryFn: () => fetchConvoyRoute(id) });
}

export function useConvoyVehicles(
  id: number,
): UseQueryResult<readonly ConvoyVehicleReadModel[] | ParentMissing> {
  return useQuery({ queryKey: qk.convoys.vehicles(id), queryFn: () => fetchConvoyVehicles(id) });
}

export function useCreateConvoy(): UseMutationResult<CreatedResource, Error, CreateConvoyRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createConvoy,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.all }),
  });
}

export function useUpdateConvoy(id: number): UseMutationResult<void, Error, UpdateConvoyRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateConvoyRequest) => updateConvoy(id, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.convoys.all });
      await queryClient.invalidateQueries({ queryKey: qk.convoys.detail(id) });
    },
  });
}

export function useDeleteConvoy(): UseMutationResult<void, Error, number> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: deleteConvoy,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.all }),
  });
}

export function useReplaceConvoyRoute(
  id: number,
): UseMutationResult<void, Error, ReplaceConvoyRouteRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: ReplaceConvoyRouteRequest) => replaceConvoyRoute(id, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.route(id) }),
  });
}

export function useAssignVehicle(id: number): UseMutationResult<void, Error, string> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (vin: string) => assignVehicle(id, vin),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.vehicles(id) }),
  });
}

export function useUnassignVehicle(id: number): UseMutationResult<void, Error, string> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (vin: string) => unassignVehicle(id, vin),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.vehicles(id) }),
  });
}

export function usePublishTruckList(id: number): UseMutationResult<void, Error, void> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => publishTruckList(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.convoys.detail(id) });
      await queryClient.invalidateQueries({ queryKey: qk.convoys.vehicles(id) });
      // A published truck list is a precondition for proposing a manifest against the convoy.
      await queryClient.invalidateQueries({ queryKey: qk.manifests.all });
    },
  });
}
