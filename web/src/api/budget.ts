import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource } from './client';
import { delete204, getJson, postCreate, put204 } from './http';
import { ApiNotFound } from './problem';
import { qk } from './queryKeys';
import {
  budgetLineReadModelSchema,
  budgetSummaryReadModelSchema,
  convoyCostReadModelSchema,
  equipmentItemReadModelSchema,
  vehicleEquipmentReadModelSchema,
} from './schemas/convoys';
import type {
  AddCostRequest,
  BudgetLineReadModel,
  BudgetSummaryReadModel,
  ConvoyCostReadModel,
  EquipmentItemReadModel,
  SetBudgetRequest,
  SetVehicleEquipmentRequest,
  VehicleEquipmentReadModel,
} from './schemas/convoys';

const idPath = (id: number) => `/convoys/${String(id)}`;
const equipmentPath = (id: number, vin: string) =>
  `${idPath(id)}/vehicles/${encodeURIComponent(vin)}/equipment`;

export const fetchBudget = (id: number): Promise<readonly BudgetLineReadModel[]> =>
  getJson(`${idPath(id)}/budget`, z.array(budgetLineReadModelSchema));

export const setBudget = (id: number, body: SetBudgetRequest): Promise<void> =>
  put204(`${idPath(id)}/budget`, body);

export const fetchBudgetSummary = (id: number): Promise<BudgetSummaryReadModel> =>
  getJson(`${idPath(id)}/budget/summary`, budgetSummaryReadModelSchema);

export const fetchCosts = (id: number): Promise<readonly ConvoyCostReadModel[]> =>
  getJson(`${idPath(id)}/costs`, z.array(convoyCostReadModelSchema));

export const addCost = (id: number, body: AddCostRequest): Promise<CreatedResource> =>
  postCreate(`${idPath(id)}/costs`, body);

export const deleteCost = (id: number, costId: number): Promise<void> =>
  delete204(`${idPath(id)}/costs/${String(costId)}`);

export const fetchEquipmentItems = (): Promise<readonly EquipmentItemReadModel[]> =>
  getJson('/equipment-items', z.array(equipmentItemReadModelSchema));

export const addEquipmentItem = (body: {
  name: string;
  unitCostGbp?: number;
}): Promise<CreatedResource> => postCreate('/equipment-items', body);

// A vehicle that is not on the convoy is a 404; the panel only shows vehicles that are.
export async function fetchVehicleEquipment(
  id: number,
  vin: string,
): Promise<readonly VehicleEquipmentReadModel[]> {
  try {
    return await getJson(equipmentPath(id, vin), z.array(vehicleEquipmentReadModelSchema));
  } catch (error) {
    if (error instanceof ApiNotFound) {
      return [];
    }
    throw error;
  }
}

export const setVehicleEquipment = (
  id: number,
  vin: string,
  body: SetVehicleEquipmentRequest,
): Promise<void> => put204(equipmentPath(id, vin), body);

export function useBudget(id: number): UseQueryResult<readonly BudgetLineReadModel[]> {
  return useQuery({ queryKey: qk.convoys.budget(id), queryFn: () => fetchBudget(id) });
}

export function useBudgetSummary(id: number): UseQueryResult<BudgetSummaryReadModel> {
  return useQuery({
    queryKey: qk.convoys.budgetSummary(id),
    queryFn: () => fetchBudgetSummary(id),
  });
}

export function useCosts(id: number): UseQueryResult<readonly ConvoyCostReadModel[]> {
  return useQuery({ queryKey: qk.convoys.costs(id), queryFn: () => fetchCosts(id) });
}

// The budget, the costs, the summary and the readiness advice all move together.
function useRefreshBudget(id: number) {
  const queryClient = useQueryClient();
  return async () => {
    await queryClient.invalidateQueries({ queryKey: qk.convoys.budget(id) });
    await queryClient.invalidateQueries({ queryKey: qk.convoys.costs(id) });
    await queryClient.invalidateQueries({ queryKey: qk.convoys.readiness(id) });
  };
}

export function useSetBudget(id: number): UseMutationResult<void, Error, SetBudgetRequest> {
  const refresh = useRefreshBudget(id);
  return useMutation({
    mutationFn: (body: SetBudgetRequest) => setBudget(id, body),
    onSuccess: refresh,
  });
}

export function useAddCost(id: number): UseMutationResult<CreatedResource, Error, AddCostRequest> {
  const refresh = useRefreshBudget(id);
  return useMutation({
    mutationFn: (body: AddCostRequest) => addCost(id, body),
    onSuccess: refresh,
  });
}

export function useDeleteCost(id: number): UseMutationResult<void, Error, number> {
  const refresh = useRefreshBudget(id);
  return useMutation({
    mutationFn: (costId: number) => deleteCost(id, costId),
    onSuccess: refresh,
  });
}

export function useEquipmentItems(): UseQueryResult<readonly EquipmentItemReadModel[]> {
  return useQuery({ queryKey: qk.convoys.equipmentItems, queryFn: fetchEquipmentItems });
}

export function useAddEquipmentItem(): UseMutationResult<
  CreatedResource,
  Error,
  { name: string; unitCostGbp?: number }
> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: addEquipmentItem,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.equipmentItems }),
  });
}

export function useVehicleEquipment(
  id: number,
  vin: string,
): UseQueryResult<readonly VehicleEquipmentReadModel[]> {
  return useQuery({
    queryKey: qk.convoys.equipment(id, vin),
    queryFn: () => fetchVehicleEquipment(id, vin),
  });
}

export function useSetVehicleEquipment(
  id: number,
  vin: string,
): UseMutationResult<void, Error, SetVehicleEquipmentRequest> {
  const refresh = useRefreshBudget(id);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SetVehicleEquipmentRequest) => setVehicleEquipment(id, vin, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.convoys.equipment(id, vin) });
      await refresh();
    },
  });
}
