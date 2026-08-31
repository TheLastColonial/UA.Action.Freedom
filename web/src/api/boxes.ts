import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource, ParentMissing } from './client';
import { delete204, getCollection, getJson, post204, postCreate, put204 } from './http';
import { qk } from './queryKeys';
import type { PageParams } from './queryKeys';
import { boxItemReadModelSchema, boxReadModelSchema } from './schemas/boxes';
import type {
  AddBoxItemRequest,
  BoxItemReadModel,
  BoxReadModel,
  CreateBoxRequest,
  UpdateBoxRequest,
  ValidateBoxRequest,
} from './schemas/boxes';

const BASE = '/boxes';
const idPath = (id: number) => `${BASE}/${String(id)}`;

export function fetchBoxes(params: PageParams): Promise<readonly BoxReadModel[]> {
  return getJson(BASE, z.array(boxReadModelSchema), {
    page: params.page,
    pageSize: params.pageSize,
  });
}

export function fetchBox(id: number): Promise<BoxReadModel> {
  return getJson(idPath(id), boxReadModelSchema);
}

export function createBox(body: CreateBoxRequest): Promise<CreatedResource> {
  return postCreate(BASE, body);
}

export function updateBox(id: number, body: UpdateBoxRequest): Promise<void> {
  return put204(idPath(id), body);
}

export function deleteBox(id: number): Promise<void> {
  return delete204(idPath(id));
}

export function fetchBoxItems(id: number): Promise<readonly BoxItemReadModel[] | ParentMissing> {
  return getCollection(`${idPath(id)}/items`, boxItemReadModelSchema);
}

export function addBoxItem(id: number, body: AddBoxItemRequest): Promise<void> {
  return post204(`${idPath(id)}/items`, body);
}

export function removeBoxItem(id: number, itemId: string): Promise<void> {
  return delete204(`${idPath(id)}/items/${encodeURIComponent(itemId)}`);
}

export function validateBox(id: number, body: ValidateBoxRequest): Promise<void> {
  return post204(`${idPath(id)}/validate`, body);
}

export function useBoxes(params: PageParams): UseQueryResult<readonly BoxReadModel[]> {
  return useQuery({ queryKey: qk.boxes.list(params), queryFn: () => fetchBoxes(params) });
}

export function useBox(id: number): UseQueryResult<BoxReadModel> {
  return useQuery({ queryKey: qk.boxes.detail(id), queryFn: () => fetchBox(id) });
}

export function useBoxItems(
  id: number,
): UseQueryResult<readonly BoxItemReadModel[] | ParentMissing> {
  return useQuery({ queryKey: qk.boxes.items(id), queryFn: () => fetchBoxItems(id) });
}

export function useCreateBox(): UseMutationResult<CreatedResource, Error, CreateBoxRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createBox,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.boxes.all }),
  });
}

export function useUpdateBox(id: number): UseMutationResult<void, Error, UpdateBoxRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateBoxRequest) => updateBox(id, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.boxes.all });
      await queryClient.invalidateQueries({ queryKey: qk.boxes.detail(id) });
    },
  });
}

export function useDeleteBox(): UseMutationResult<void, Error, number> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: deleteBox,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.boxes.all }),
  });
}

export function useAddBoxItem(id: number): UseMutationResult<void, Error, AddBoxItemRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: AddBoxItemRequest) => addBoxItem(id, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.boxes.items(id) }),
  });
}

export function useRemoveBoxItem(id: number): UseMutationResult<void, Error, string> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (itemId: string) => removeBoxItem(id, itemId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.boxes.items(id) }),
  });
}

export function useValidateBox(id: number): UseMutationResult<void, Error, ValidateBoxRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: ValidateBoxRequest) => validateBox(id, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.boxes.detail(id) });
      await queryClient.invalidateQueries({ queryKey: qk.boxes.items(id) });
      await queryClient.invalidateQueries({ queryKey: qk.boxes.all });
    },
  });
}
