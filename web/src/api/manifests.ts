import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource, ParentMissing } from './client';
import { delete204, getCollection, getJson, postCreate, postTransition, put204 } from './http';
import { qk } from './queryKeys';
import type { PageParams } from './queryKeys';
import type { ManifestLeg } from './schemas/common';
import {
  manifestBoxReadModelSchema,
  manifestDriverTeamReadModelSchema,
  manifestReadModelSchema,
  manifestWeightReadModelSchema,
} from './schemas/manifests';
import type {
  CreateManifestRequest,
  ManifestBoxReadModel,
  ManifestDriverTeamReadModel,
  ManifestReadModel,
  ManifestWeightReadModel,
  SetManifestTeamRequest,
  UpdateManifestRequest,
} from './schemas/manifests';
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

export function createManifest(body: CreateManifestRequest): Promise<CreatedResource> {
  return postCreate(BASE, body);
}

export function updateManifest(id: string, body: UpdateManifestRequest): Promise<void> {
  return put204(idPath(id), body);
}

export function deleteManifest(id: string): Promise<void> {
  return delete204(idPath(id));
}

export function fetchManifestTeams(
  id: string,
): Promise<readonly ManifestDriverTeamReadModel[] | ParentMissing> {
  return getCollection(`${idPath(id)}/teams`, manifestDriverTeamReadModelSchema);
}

export function setManifestTeam(
  id: string,
  leg: ManifestLeg,
  body: SetManifestTeamRequest,
): Promise<void> {
  return put204(`${idPath(id)}/teams/${leg}`, body);
}

export function fetchManifestBoxes(
  id: string,
): Promise<readonly ManifestBoxReadModel[] | ParentMissing> {
  return getCollection(`${idPath(id)}/boxes`, manifestBoxReadModelSchema);
}

export function attachManifestBox(id: string, boxId: number): Promise<void> {
  return put204(`${idPath(id)}/boxes/${String(boxId)}`);
}

export function detachManifestBox(id: string, boxId: number): Promise<void> {
  return delete204(`${idPath(id)}/boxes/${String(boxId)}`);
}

export function fetchManifestWeight(id: string): Promise<ManifestWeightReadModel> {
  return getJson(`${idPath(id)}/weight`, manifestWeightReadModelSchema);
}

export function transitionManifest(id: string, verb: ManifestVerb): Promise<void> {
  return postTransition(`${idPath(id)}/${verb}`);
}

export function useManifests(params: PageParams): UseQueryResult<readonly ManifestReadModel[]> {
  return useQuery({ queryKey: qk.manifests.list(params), queryFn: () => fetchManifests(params) });
}

export function useManifest(id: string): UseQueryResult<ManifestReadModel> {
  return useQuery({ queryKey: qk.manifests.detail(id), queryFn: () => fetchManifest(id) });
}

export function useManifestTeams(
  id: string,
): UseQueryResult<readonly ManifestDriverTeamReadModel[] | ParentMissing> {
  return useQuery({ queryKey: qk.manifests.teams(id), queryFn: () => fetchManifestTeams(id) });
}

export function useManifestBoxes(
  id: string,
): UseQueryResult<readonly ManifestBoxReadModel[] | ParentMissing> {
  return useQuery({ queryKey: qk.manifests.boxes(id), queryFn: () => fetchManifestBoxes(id) });
}

export function useManifestWeight(id: string): UseQueryResult<ManifestWeightReadModel> {
  return useQuery({ queryKey: qk.manifests.weight(id), queryFn: () => fetchManifestWeight(id) });
}

export function useCreateManifest(): UseMutationResult<
  CreatedResource,
  Error,
  CreateManifestRequest
> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createManifest,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.manifests.all }),
  });
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

export function useSetManifestTeam(
  id: string,
  leg: ManifestLeg,
): UseMutationResult<void, Error, SetManifestTeamRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SetManifestTeamRequest) => setManifestTeam(id, leg, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.manifests.teams(id) }),
  });
}

export function useAttachManifestBox(id: string): UseMutationResult<void, Error, number> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (boxId: number) => attachManifestBox(id, boxId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.manifests.boxes(id) });
      await queryClient.invalidateQueries({ queryKey: qk.manifests.weight(id) });
    },
  });
}

export function useDetachManifestBox(id: string): UseMutationResult<void, Error, number> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (boxId: number) => detachManifestBox(id, boxId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.manifests.boxes(id) });
      await queryClient.invalidateQueries({ queryKey: qk.manifests.weight(id) });
    },
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
