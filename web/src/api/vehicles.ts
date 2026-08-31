import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource } from './client';
import { delete204, getJson, postCreate, put204 } from './http';
import { qk } from './queryKeys';
import type { PageParams } from './queryKeys';
import { vehicleReadModelSchema } from './schemas/vehicles';
import type {
  CreateVehicleRequest,
  UpdateVehicleRequest,
  VehicleReadModel,
} from './schemas/vehicles';

const BASE = '/vehicles';
const vinPath = (vin: string) => `${BASE}/${encodeURIComponent(vin)}`;

export function fetchVehicles(params: PageParams): Promise<readonly VehicleReadModel[]> {
  return getJson(BASE, z.array(vehicleReadModelSchema), {
    page: params.page,
    pageSize: params.pageSize,
  });
}

export function fetchVehicle(vin: string): Promise<VehicleReadModel> {
  return getJson(vinPath(vin), vehicleReadModelSchema);
}

export function createVehicle(body: CreateVehicleRequest): Promise<CreatedResource> {
  return postCreate(BASE, body);
}

export function updateVehicle(vin: string, body: UpdateVehicleRequest): Promise<void> {
  return put204(vinPath(vin), body);
}

export function deleteVehicle(vin: string): Promise<void> {
  return delete204(vinPath(vin));
}

export function useVehicles(params: PageParams): UseQueryResult<readonly VehicleReadModel[]> {
  return useQuery({
    queryKey: qk.vehicles.list(params),
    queryFn: () => fetchVehicles(params),
  });
}

export function useVehicle(vin: string): UseQueryResult<VehicleReadModel> {
  return useQuery({
    queryKey: qk.vehicles.detail(vin),
    queryFn: () => fetchVehicle(vin),
  });
}

export function useCreateVehicle(): UseMutationResult<
  CreatedResource,
  Error,
  CreateVehicleRequest
> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createVehicle,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.vehicles.all }),
  });
}

export function useUpdateVehicle(
  vin: string,
): UseMutationResult<void, Error, UpdateVehicleRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateVehicleRequest) => updateVehicle(vin, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.vehicles.all });
      await queryClient.invalidateQueries({ queryKey: qk.vehicles.detail(vin) });
    },
  });
}

export function useDeleteVehicle(): UseMutationResult<void, Error, string> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: deleteVehicle,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.vehicles.all }),
  });
}
