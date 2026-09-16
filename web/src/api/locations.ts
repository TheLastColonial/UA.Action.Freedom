import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource, ParentMissing } from './client';
import { delete204, getCollection, getJson, postCreate, put204 } from './http';
import { qk } from './queryKeys';
import type { PageParams } from './queryKeys';
import { bayReadModelSchema, locationReadModelSchema } from './schemas/locations';
import type {
  BayReadModel,
  CreateBayRequest,
  CreateLocationRequest,
  LocationReadModel,
  UpdateBayRequest,
  UpdateLocationRequest,
} from './schemas/locations';

const BASE = '/locations';
const idPath = (id: number) => `${BASE}/${String(id)}`;
const bayPath = (id: number, bayId: number) => `${idPath(id)}/bays/${String(bayId)}`;

export function fetchLocations(params: PageParams): Promise<readonly LocationReadModel[]> {
  return getJson(BASE, z.array(locationReadModelSchema), {
    page: params.page,
    pageSize: params.pageSize,
  });
}

export function fetchLocation(id: number): Promise<LocationReadModel> {
  return getJson(idPath(id), locationReadModelSchema);
}

export function createLocation(body: CreateLocationRequest): Promise<CreatedResource> {
  return postCreate(BASE, body);
}

export function updateLocation(id: number, body: UpdateLocationRequest): Promise<void> {
  return put204(idPath(id), body);
}

export function deleteLocation(id: number): Promise<void> {
  return delete204(idPath(id));
}

export function fetchBays(id: number): Promise<readonly BayReadModel[] | ParentMissing> {
  return getCollection(`${idPath(id)}/bays`, bayReadModelSchema);
}

export function createBay(id: number, body: CreateBayRequest): Promise<CreatedResource> {
  return postCreate(`${idPath(id)}/bays`, body);
}

export function updateBay(id: number, bayId: number, body: UpdateBayRequest): Promise<void> {
  return put204(bayPath(id, bayId), body);
}

export function deleteBay(id: number, bayId: number): Promise<void> {
  return delete204(bayPath(id, bayId));
}

export function useLocations(params: PageParams): UseQueryResult<readonly LocationReadModel[]> {
  return useQuery({ queryKey: qk.locations.list(params), queryFn: () => fetchLocations(params) });
}

export function useLocation(id: number): UseQueryResult<LocationReadModel> {
  return useQuery({ queryKey: qk.locations.detail(id), queryFn: () => fetchLocation(id) });
}

export function useBays(
  id: number,
  options: { enabled?: boolean } = {},
): UseQueryResult<readonly BayReadModel[] | ParentMissing> {
  return useQuery({
    queryKey: qk.locations.bays(id),
    queryFn: () => fetchBays(id),
    enabled: options.enabled ?? true,
  });
}

export function useCreateLocation(): UseMutationResult<
  CreatedResource,
  Error,
  CreateLocationRequest
> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createLocation,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.locations.all }),
  });
}

export function useUpdateLocation(
  id: number,
): UseMutationResult<void, Error, UpdateLocationRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateLocationRequest) => updateLocation(id, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.locations.all });
      await queryClient.invalidateQueries({ queryKey: qk.locations.detail(id) });
    },
  });
}

export function useDeleteLocation(): UseMutationResult<void, Error, number> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: deleteLocation,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.locations.all }),
  });
}

export function useCreateBay(
  id: number,
): UseMutationResult<CreatedResource, Error, CreateBayRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateBayRequest) => createBay(id, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.locations.bays(id) }),
  });
}

export function useUpdateBay(
  id: number,
  bayId: number,
): UseMutationResult<void, Error, UpdateBayRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateBayRequest) => updateBay(id, bayId, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.locations.bays(id) }),
  });
}

export function useDeleteBay(id: number): UseMutationResult<void, Error, number> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (bayId: number) => deleteBay(id, bayId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.locations.bays(id) }),
  });
}
