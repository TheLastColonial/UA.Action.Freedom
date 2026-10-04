import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import { delete204, getJson, post204, postTransition, put204 } from './http';
import { ApiNotFound } from './problem';
import { qk } from './queryKeys';
import {
  KIND_SEGMENT,
  declarationReadModelSchema,
  ensDeclarationReadModelSchema,
  redeclareTaskSchema,
} from './schemas/declarations';
import type {
  DeclarationKind,
  DeclarationReadModel,
  EnsDeclarationReadModel,
  RecordDeclarationRequest,
  RecordEnsRequest,
  RedeclareTask,
  RefuseDeclarationRequest,
} from './schemas/declarations';

const basePath = (convoyId: number, vin: string) =>
  `/convoys/${convoyId}/vehicles/${encodeURIComponent(vin)}/declarations`;

export function fetchDeclarations(
  convoyId: number,
  vin: string,
): Promise<readonly DeclarationReadModel[]> {
  return getJson(basePath(convoyId, vin), z.array(declarationReadModelSchema));
}

// No ENS recorded is a bare 404 — an answer ("not filed"), not an error — so it resolves to null.
export async function fetchEns(
  convoyId: number,
  vin: string,
): Promise<EnsDeclarationReadModel | null> {
  try {
    return await getJson(`${basePath(convoyId, vin)}/ens`, ensDeclarationReadModelSchema);
  } catch (error) {
    if (error instanceof ApiNotFound) {
      return null;
    }
    throw error;
  }
}

export function recordEns(convoyId: number, vin: string, body: RecordEnsRequest): Promise<void> {
  return put204(`${basePath(convoyId, vin)}/ens`, body);
}

export function withdrawEns(convoyId: number, vin: string): Promise<void> {
  return delete204(`${basePath(convoyId, vin)}/ens`);
}

export function recordDeclaration(
  convoyId: number,
  vin: string,
  kind: DeclarationKind,
  body: RecordDeclarationRequest,
): Promise<void> {
  return post204(`${basePath(convoyId, vin)}/${KIND_SEGMENT[kind]}/record`, body);
}

export function refuseDeclaration(
  convoyId: number,
  vin: string,
  kind: DeclarationKind,
  body: RefuseDeclarationRequest,
): Promise<void> {
  return post204(`${basePath(convoyId, vin)}/${KIND_SEGMENT[kind]}/refused`, body);
}

// Answered 409 "record the reference instead" while the authority's submission mode is manual.
export function fileDeclaration(
  convoyId: number,
  vin: string,
  kind: DeclarationKind,
): Promise<void> {
  return postTransition(`${basePath(convoyId, vin)}/${KIND_SEGMENT[kind]}/file`);
}

export function useDeclarations(
  convoyId: number,
  vin: string,
): UseQueryResult<readonly DeclarationReadModel[]> {
  return useQuery({
    queryKey: qk.declarations.list(convoyId, vin),
    queryFn: () => fetchDeclarations(convoyId, vin),
  });
}

export function useEns(
  convoyId: number,
  vin: string,
): UseQueryResult<EnsDeclarationReadModel | null> {
  return useQuery({
    queryKey: qk.declarations.ens(convoyId, vin),
    queryFn: () => fetchEns(convoyId, vin),
  });
}

function useInvalidateDeclarations(convoyId: number, vin: string): () => Promise<void> {
  const queryClient = useQueryClient();
  return async () => {
    await queryClient.invalidateQueries({ queryKey: qk.declarations.vehicle(convoyId, vin) });
    await queryClient.invalidateQueries({ queryKey: qk.declarations.tasks(convoyId) });
  };
}

export function useRecordEns(
  convoyId: number,
  vin: string,
): UseMutationResult<void, Error, RecordEnsRequest> {
  const invalidate = useInvalidateDeclarations(convoyId, vin);
  return useMutation({
    mutationFn: (body: RecordEnsRequest) => recordEns(convoyId, vin, body),
    onSuccess: invalidate,
  });
}

export function useWithdrawEns(
  convoyId: number,
  vin: string,
): UseMutationResult<void, Error, void> {
  const invalidate = useInvalidateDeclarations(convoyId, vin);
  return useMutation({ mutationFn: () => withdrawEns(convoyId, vin), onSuccess: invalidate });
}

interface RecordVariables {
  kind: DeclarationKind;
  body: RecordDeclarationRequest;
}

export function useRecordDeclaration(
  convoyId: number,
  vin: string,
): UseMutationResult<void, Error, RecordVariables> {
  const invalidate = useInvalidateDeclarations(convoyId, vin);
  return useMutation({
    mutationFn: ({ kind, body }: RecordVariables) => recordDeclaration(convoyId, vin, kind, body),
    onSuccess: invalidate,
  });
}

interface RefuseVariables {
  kind: DeclarationKind;
  body: RefuseDeclarationRequest;
}

export function useRefuseDeclaration(
  convoyId: number,
  vin: string,
): UseMutationResult<void, Error, RefuseVariables> {
  const invalidate = useInvalidateDeclarations(convoyId, vin);
  return useMutation({
    mutationFn: ({ kind, body }: RefuseVariables) => refuseDeclaration(convoyId, vin, kind, body),
    onSuccess: invalidate,
  });
}

export function useFileDeclaration(
  convoyId: number,
  vin: string,
): UseMutationResult<void, Error, DeclarationKind> {
  const invalidate = useInvalidateDeclarations(convoyId, vin);
  return useMutation({
    mutationFn: (kind: DeclarationKind) => fileDeclaration(convoyId, vin, kind),
    onSuccess: invalidate,
  });
}

// The Dispatcher's re-declare tasks for a convoy: one per stale declaration, derived on read.
export function fetchRedeclareTasks(convoyId: number): Promise<readonly RedeclareTask[]> {
  return getJson(`/convoys/${convoyId}/tasks`, z.array(redeclareTaskSchema));
}

export function useRedeclareTasks(convoyId: number): UseQueryResult<readonly RedeclareTask[]> {
  return useQuery({
    queryKey: qk.declarations.tasks(convoyId),
    queryFn: () => fetchRedeclareTasks(convoyId),
  });
}

// Keeps the stale declaration and its reference as history and starts a new draft for the same scope.
export function withdrawDeclaration(
  convoyId: number,
  vin: string,
  declarationId: number,
): Promise<void> {
  return postTransition(`${basePath(convoyId, vin)}/${String(declarationId)}/withdraw`);
}

export function useWithdrawDeclaration(
  convoyId: number,
  vin: string,
): UseMutationResult<void, Error, number> {
  const invalidate = useInvalidateDeclarations(convoyId, vin);
  return useMutation({
    mutationFn: (declarationId: number) => withdrawDeclaration(convoyId, vin, declarationId),
    onSuccess: invalidate,
  });
}
