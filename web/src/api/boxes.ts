import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource, ParentMissing } from './client';
import {
  delete204,
  getCollection,
  getJson,
  getText,
  post201,
  post204,
  postCreate,
  put204,
} from './http';
import { ApiNotFound } from './problem';
import { qk } from './queryKeys';
import type { PageParams } from './queryKeys';
import {
  boxBayAssignmentReadModelSchema,
  boxItemReadModelSchema,
  boxQrCodeReadModelSchema,
  boxReadModelSchema,
} from './schemas/boxes';
import type {
  AddBoxItemRequest,
  AssignBoxBayRequest,
  BoxBayAssignmentReadModel,
  BoxItemReadModel,
  BoxQrCodeReadModel,
  BoxReadModel,
  CreateBoxRequest,
  UpdateBoxItemRequest,
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

export function updateBoxItem(
  id: number,
  itemId: string,
  body: UpdateBoxItemRequest,
): Promise<void> {
  return put204(`${idPath(id)}/items/${encodeURIComponent(itemId)}`, body);
}

export function removeBoxItem(id: number, itemId: string): Promise<void> {
  return delete204(`${idPath(id)}/items/${encodeURIComponent(itemId)}`);
}

export function validateBox(id: number, body: ValidateBoxRequest): Promise<void> {
  return post204(`${idPath(id)}/validate`, body);
}

/** The box's active QR label, or `null` when it has none. */
export async function fetchBoxQrCode(id: number): Promise<BoxQrCodeReadModel | null> {
  try {
    return await getJson(`${idPath(id)}/qr-code`, boxQrCodeReadModelSchema);
  } catch (error) {
    if (error instanceof ApiNotFound) {
      return null;
    }
    throw error;
  }
}

/** Issue (or re-issue) the box's QR label. Re-issuing revokes whatever it had. */
export function issueBoxQrCode(id: number): Promise<CreatedResource> {
  return post201(`${idPath(id)}/qr-code`);
}

export function revokeBoxQrCode(id: number): Promise<void> {
  return delete204(`${idPath(id)}/qr-code`);
}

/** The printable label as an SVG document. */
export function fetchBoxLabel(id: number): Promise<string> {
  return getText(`${idPath(id)}/label`);
}

/** The bay this box currently occupies, or `null` when it is not in one. */
export async function fetchBoxBay(id: number): Promise<BoxBayAssignmentReadModel | null> {
  try {
    return await getJson(`${idPath(id)}/bay`, boxBayAssignmentReadModelSchema);
  } catch (error) {
    if (error instanceof ApiNotFound) {
      return null;
    }
    throw error;
  }
}

export function fetchBoxBayHistory(id: number): Promise<readonly BoxBayAssignmentReadModel[]> {
  return getJson(`${idPath(id)}/bay/history`, z.array(boxBayAssignmentReadModelSchema));
}

/** Place (or move) the box in a bay. Assigning a new bay vacates whatever it was already in. */
export function assignBoxBay(id: number, body: AssignBoxBayRequest): Promise<void> {
  return put204(`${idPath(id)}/bay`, body);
}

export function vacateBoxBay(id: number): Promise<void> {
  return delete204(`${idPath(id)}/bay`);
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

export function useUpdateBoxItem(
  id: number,
): UseMutationResult<void, Error, { itemId: string; body: UpdateBoxItemRequest }> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ itemId, body }) => updateBoxItem(id, itemId, body),
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

export function useBoxQrCode(id: number): UseQueryResult<BoxQrCodeReadModel | null> {
  return useQuery({ queryKey: qk.boxes.qrCode(id), queryFn: () => fetchBoxQrCode(id) });
}

export function useBoxLabel(id: number, enabled: boolean): UseQueryResult<string> {
  return useQuery({ queryKey: qk.boxes.label(id), queryFn: () => fetchBoxLabel(id), enabled });
}

function useQrCodeMutation(
  id: number,
  mutationFn: () => Promise<unknown>,
): UseMutationResult<unknown, Error, void> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.boxes.qrCode(id) });
      await queryClient.invalidateQueries({ queryKey: qk.boxes.label(id) });
    },
  });
}

export function useIssueBoxQrCode(id: number): UseMutationResult<unknown, Error, void> {
  return useQrCodeMutation(id, () => issueBoxQrCode(id));
}

export function useRevokeBoxQrCode(id: number): UseMutationResult<unknown, Error, void> {
  return useQrCodeMutation(id, () => revokeBoxQrCode(id));
}

export function useBoxBay(id: number): UseQueryResult<BoxBayAssignmentReadModel | null> {
  return useQuery({ queryKey: qk.boxes.bay(id), queryFn: () => fetchBoxBay(id) });
}

export function useBoxBayHistory(id: number): UseQueryResult<readonly BoxBayAssignmentReadModel[]> {
  return useQuery({ queryKey: qk.boxes.bayHistory(id), queryFn: () => fetchBoxBayHistory(id) });
}

export function useAssignBoxBay(id: number): UseMutationResult<void, Error, AssignBoxBayRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: AssignBoxBayRequest) => assignBoxBay(id, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.boxes.bay(id) });
      await queryClient.invalidateQueries({ queryKey: qk.boxes.bayHistory(id) });
    },
  });
}

export function useVacateBoxBay(id: number): UseMutationResult<void, Error, void> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => vacateBoxBay(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.boxes.bay(id) });
      await queryClient.invalidateQueries({ queryKey: qk.boxes.bayHistory(id) });
    },
  });
}
