import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource } from './client';
import { delete204, getJson, postCreate, put204 } from './http';
import { qk } from './queryKeys';
import type { PeopleListParams } from './queryKeys';
import { personReadModelSchema } from './schemas/people';
import type { CreatePersonRequest, PersonReadModel, UpdatePersonRequest } from './schemas/people';

const BASE = '/people';
const idPath = (id: string) => `${BASE}/${encodeURIComponent(id)}`;

export function fetchPeople(params: PeopleListParams): Promise<readonly PersonReadModel[]> {
  return getJson(BASE, z.array(personReadModelSchema), {
    page: params.page,
    pageSize: params.pageSize,
    driversOnly: params.driversOnly,
  });
}

export function fetchPerson(id: string): Promise<PersonReadModel> {
  return getJson(idPath(id), personReadModelSchema);
}

export function createPerson(body: CreatePersonRequest): Promise<CreatedResource> {
  return postCreate(BASE, body);
}

export function updatePerson(id: string, body: UpdatePersonRequest): Promise<void> {
  return put204(idPath(id), body);
}

export function deletePerson(id: string): Promise<void> {
  return delete204(idPath(id));
}

export function usePeople(params: PeopleListParams): UseQueryResult<readonly PersonReadModel[]> {
  return useQuery({ queryKey: qk.people.list(params), queryFn: () => fetchPeople(params) });
}

export function usePerson(id: string): UseQueryResult<PersonReadModel> {
  return useQuery({ queryKey: qk.people.detail(id), queryFn: () => fetchPerson(id) });
}

export function useCreatePerson(): UseMutationResult<CreatedResource, Error, CreatePersonRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createPerson,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.people.all }),
  });
}

export function useUpdatePerson(id: string): UseMutationResult<void, Error, UpdatePersonRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdatePersonRequest) => updatePerson(id, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.people.all });
      await queryClient.invalidateQueries({ queryKey: qk.people.detail(id) });
    },
  });
}

export function useDeletePerson(): UseMutationResult<void, Error, string> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: deletePerson,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.people.all }),
  });
}
