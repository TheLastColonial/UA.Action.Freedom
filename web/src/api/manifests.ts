import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { ParentMissing } from './client';
import { delete204, getCollection, getJson, postTransition, put204 } from './http';
import { qk } from './queryKeys';
import type { PageParams } from './queryKeys';
import {
  ensDeclarationReadModelSchema,
  manifestBoxReadModelSchema,
  manifestReadModelSchema,
  manifestWeightReadModelSchema,
} from './schemas/manifests';
import type {
  EnsDeclarationReadModel,
  ManifestBoxReadModel,
  ManifestReadModel,
  ManifestWeightReadModel,
  RecordEnsRequest,
  UpdateManifestRequest,
} from './schemas/manifests';
import { ApiNotFound } from './problem';
import { vehicleCrewReadModelSchema } from './schemas/convoys';
import type { VehicleCrewReadModel } from './schemas/convoys';
import type { ManifestVerb } from '../pages/manifests/transitions';

const BASE = '/manifests';
const idPath = (id: string) => `${BASE}/${encodeURIComponent(id)}`;

export function fetchManifests(params: PageParams): Promise<readonly ManifestReadModel[]> {
  return getJson(BASE, z.array(manifestReadModelSchema), {
    page: params.page,
    pageSize: params.pageSize,
  });
}

export function fetchManifest(id: string): Promise<ManifestReadModel> {
  return getJson(idPath(id), manifestReadModelSchema);
}

export function updateManifest(id: string, body: UpdateManifestRequest): Promise<void> {
  return put204(idPath(id), body);
}

export function deleteManifest(id: string): Promise<void> {
  return delete204(idPath(id));
}

// A read. Crewing happens on the truck-list entry, in the convoy slice — the manifest used to
// own a second, unconnected crew record.
export function fetchManifestCrew(
  id: string,
): Promise<readonly VehicleCrewReadModel[] | ParentMissing> {
  return getCollection(`${idPath(id)}/crew`, vehicleCrewReadModelSchema);
}

export function fetchManifestBoxes(
  id: string,
): Promise<readonly ManifestBoxReadModel[] | ParentMissing> {
  return getCollection(`${idPath(id)}/boxes`, manifestBoxReadModelSchema);
}

export function fetchManifestWeight(id: string): Promise<ManifestWeightReadModel> {
  return getJson(`${idPath(id)}/weight`, manifestWeightReadModelSchema);
}

export function transitionManifest(id: string, verb: ManifestVerb): Promise<void> {
  return postTransition(`${idPath(id)}/${verb}`);
}

// No declaration recorded is a bare 404 — an answer ("not filed"), not an error — so it resolves
// to null, the same convention as a vehicle's insurance.
export async function fetchManifestEns(id: string): Promise<EnsDeclarationReadModel | null> {
  try {
    return await getJson(`${idPath(id)}/ens`, ensDeclarationReadModelSchema);
  } catch (error) {
    if (error instanceof ApiNotFound) {
      return null;
    }
    throw error;
  }
}

export function recordManifestEns(id: string, body: RecordEnsRequest): Promise<void> {
  return put204(`${idPath(id)}/ens`, body);
}

export function withdrawManifestEns(id: string): Promise<void> {
  return delete204(`${idPath(id)}/ens`);
}

export function useManifests(params: PageParams): UseQueryResult<readonly ManifestReadModel[]> {
  return useQuery({ queryKey: qk.manifests.list(params), queryFn: () => fetchManifests(params) });
}

export function useManifest(id: string): UseQueryResult<ManifestReadModel> {
  return useQuery({ queryKey: qk.manifests.detail(id), queryFn: () => fetchManifest(id) });
}

export function useManifestCrew(
  id: string,
): UseQueryResult<readonly VehicleCrewReadModel[] | ParentMissing> {
  return useQuery({ queryKey: qk.manifests.crew(id), queryFn: () => fetchManifestCrew(id) });
}

export function useManifestBoxes(
  id: string,
): UseQueryResult<readonly ManifestBoxReadModel[] | ParentMissing> {
  return useQuery({ queryKey: qk.manifests.boxes(id), queryFn: () => fetchManifestBoxes(id) });
}

export function useManifestWeight(id: string): UseQueryResult<ManifestWeightReadModel> {
  return useQuery({ queryKey: qk.manifests.weight(id), queryFn: () => fetchManifestWeight(id) });
}

export function useUpdateManifest(
  id: string,
): UseMutationResult<void, Error, UpdateManifestRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateManifestRequest) => updateManifest(id, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.manifests.all });
      await queryClient.invalidateQueries({ queryKey: qk.manifests.detail(id) });
    },
  });
}

export function useDeleteManifest(): UseMutationResult<void, Error, string> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: deleteManifest,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.manifests.all }),
  });
}

export function useTransitionManifest(id: string): UseMutationResult<void, Error, ManifestVerb> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (verb: ManifestVerb) => transitionManifest(id, verb),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.manifests.detail(id) });
      await queryClient.invalidateQueries({ queryKey: qk.manifests.weight(id) });
      await queryClient.invalidateQueries({ queryKey: qk.manifests.all });
    },
  });
}

export function useManifestEns(id: string): UseQueryResult<EnsDeclarationReadModel | null> {
  return useQuery({ queryKey: qk.manifests.ens(id), queryFn: () => fetchManifestEns(id) });
}

export function useRecordManifestEns(id: string): UseMutationResult<void, Error, RecordEnsRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: RecordEnsRequest) => recordManifestEns(id, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.manifests.ens(id) }),
  });
}

export function useWithdrawManifestEns(id: string): UseMutationResult<void, Error, void> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => withdrawManifestEns(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.manifests.ens(id) }),
  });
}
