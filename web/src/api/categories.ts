import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource } from './client';
import { getJson, postCreate, put204 } from './http';
import { qk } from './queryKeys';
import { itemCategoryReadModelSchema } from './schemas/categories';
import type {
  CreateCategoryRequest,
  CustomsAuthority,
  ItemCategoryReadModel,
  SetCategoryCodeRequest,
  UpdateCategoryRequest,
} from './schemas/categories';

const BASE = '/categories';
const idPath = (id: number) => `${BASE}/${String(id)}`;

export function fetchCategories(): Promise<readonly ItemCategoryReadModel[]> {
  return getJson(BASE, z.array(itemCategoryReadModelSchema));
}

export function fetchCategory(id: number): Promise<ItemCategoryReadModel> {
  return getJson(idPath(id), itemCategoryReadModelSchema);
}

export function createCategory(body: CreateCategoryRequest): Promise<CreatedResource> {
  return postCreate(BASE, body);
}

export function updateCategory(id: number, body: UpdateCategoryRequest): Promise<void> {
  return put204(idPath(id), body);
}

export function setCategoryCode(
  id: number,
  authority: CustomsAuthority,
  body: SetCategoryCodeRequest,
): Promise<void> {
  return put204(`${idPath(id)}/codes/${authority}`, body);
}

export function useCategories(): UseQueryResult<readonly ItemCategoryReadModel[]> {
  return useQuery({ queryKey: qk.categories.list, queryFn: fetchCategories });
}

export function useCategory(id: number): UseQueryResult<ItemCategoryReadModel> {
  return useQuery({ queryKey: qk.categories.detail(id), queryFn: () => fetchCategory(id) });
}

export function useCreateCategory(): UseMutationResult<
  CreatedResource,
  Error,
  CreateCategoryRequest
> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createCategory,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.categories.all }),
  });
}

export function useUpdateCategory(
  id: number,
): UseMutationResult<void, Error, UpdateCategoryRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateCategoryRequest) => updateCategory(id, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.categories.all }),
  });
}

export function useSetCategoryCode(
  id: number,
): UseMutationResult<void, Error, { authority: CustomsAuthority; code: string | null }> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ authority, code }) => setCategoryCode(id, authority, { code }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.categories.all });
      // Items read their category's name and flags, and the filing sheet its codes.
      await queryClient.invalidateQueries({ queryKey: qk.boxes.all });
    },
  });
}
