import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource } from './client';
import { delete204, getJson, postCreate, put204 } from './http';
import { qk } from './queryKeys';
import type { PageParams } from './queryKeys';
import { receiverReadModelSchema, receiverUsageSchema } from './schemas/receivers';
import type {
  CreateReceiverRequest,
  ReceiverReadModel,
  ReceiverUsage,
  SetReceiverStatusRequest,
  UpdateReceiverRequest,
} from './schemas/receivers';

const BASE = '/receivers';
const refPath = (ref: string) => `${BASE}/${encodeURIComponent(ref)}`;

export function fetchReceivers(params: PageParams): Promise<readonly ReceiverReadModel[]> {
  return getJson(BASE, z.array(receiverReadModelSchema), {
    page: params.page,
    pageSize: params.pageSize,
  });
}

export function fetchReceiver(ref: string): Promise<ReceiverReadModel> {
  return getJson(refPath(ref), receiverReadModelSchema);
}

export function createReceiver(body: CreateReceiverRequest): Promise<CreatedResource> {
  return postCreate(BASE, body);
}

export function updateReceiver(ref: string, body: UpdateReceiverRequest): Promise<void> {
  return put204(refPath(ref), body);
}

// Administrator only (`receivers:register`): registration is an act of authorisation, so the Ground
// Officer who records a receiver cannot grant it.
export function setReceiverStatus(ref: string, body: SetReceiverStatusRequest): Promise<void> {
  return put204(`${refPath(ref)}/status`, body);
}

export function fetchReceiverUsage(ref: string): Promise<ReceiverUsage> {
  return getJson(`${refPath(ref)}/usage`, receiverUsageSchema);
}

// Behind `receivers:detail` on the API — deleting a receiver also removes its address.
export function deleteReceiver(ref: string): Promise<void> {
  return delete204(refPath(ref));
}

export function useReceivers(params: PageParams): UseQueryResult<readonly ReceiverReadModel[]> {
  return useQuery({ queryKey: qk.receivers.list(params), queryFn: () => fetchReceivers(params) });
}

export function useReceiver(ref: string): UseQueryResult<ReceiverReadModel> {
  return useQuery({ queryKey: qk.receivers.detail(ref), queryFn: () => fetchReceiver(ref) });
}

export function useReceiverUsage(ref: string): UseQueryResult<ReceiverUsage> {
  return useQuery({ queryKey: qk.receivers.usage(ref), queryFn: () => fetchReceiverUsage(ref) });
}

export function useSetReceiverStatus(
  ref: string,
): UseMutationResult<void, Error, SetReceiverStatusRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SetReceiverStatusRequest) => setReceiverStatus(ref, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.receivers.all });
      await queryClient.invalidateQueries({ queryKey: qk.receivers.usage(ref) });
    },
  });
}

export function useCreateReceiver(): UseMutationResult<
  CreatedResource,
  Error,
  CreateReceiverRequest
> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createReceiver,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.receivers.all }),
  });
}

export function useUpdateReceiver(
  ref: string,
): UseMutationResult<void, Error, UpdateReceiverRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateReceiverRequest) => updateReceiver(ref, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.receivers.all });
      await queryClient.invalidateQueries({ queryKey: qk.receivers.detail(ref) });
    },
  });
}

export function useDeleteReceiver(): UseMutationResult<void, Error, string> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: deleteReceiver,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.receivers.all }),
  });
}
