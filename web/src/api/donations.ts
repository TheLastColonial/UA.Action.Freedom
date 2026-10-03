import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource } from './client';
import { delete204, getJson, postCreate, put204 } from './http';
import { qk } from './queryKeys';
import type { PageParams } from './queryKeys';
import {
  donationReadModelSchema,
  donorReadModelSchema,
  donorReportSchema,
} from './schemas/donations';
import type {
  CreateDonationRequest,
  DonationReadModel,
  DonorReadModel,
  DonorReport,
  DonorRequest,
} from './schemas/donations';

const DONORS = '/donors';
const DONATIONS = '/donations';
const donorPath = (id: string) => `${DONORS}/${encodeURIComponent(id)}`;

export function fetchDonors(params: PageParams): Promise<readonly DonorReadModel[]> {
  return getJson(DONORS, z.array(donorReadModelSchema), {
    page: params.page,
    pageSize: params.pageSize,
  });
}

export function fetchDonor(id: string): Promise<DonorReadModel> {
  return getJson(donorPath(id), donorReadModelSchema);
}

export function fetchDonorDonations(id: string): Promise<readonly DonationReadModel[]> {
  return getJson(`${donorPath(id)}/donations`, z.array(donationReadModelSchema));
}

export function fetchDonorReport(id: string): Promise<DonorReport> {
  return getJson(`${donorPath(id)}/report`, donorReportSchema);
}

export function fetchDonations(params: PageParams): Promise<readonly DonationReadModel[]> {
  return getJson(DONATIONS, z.array(donationReadModelSchema), {
    page: params.page,
    pageSize: params.pageSize,
  });
}

export function useDonors(params: PageParams): UseQueryResult<readonly DonorReadModel[]> {
  return useQuery({ queryKey: qk.donors.list(params), queryFn: () => fetchDonors(params) });
}

export function useDonor(id: string): UseQueryResult<DonorReadModel> {
  return useQuery({ queryKey: qk.donors.detail(id), queryFn: () => fetchDonor(id) });
}

export function useDonorDonations(id: string): UseQueryResult<readonly DonationReadModel[]> {
  return useQuery({ queryKey: qk.donors.donations(id), queryFn: () => fetchDonorDonations(id) });
}

export function useDonorReport(id: string): UseQueryResult<DonorReport> {
  return useQuery({ queryKey: qk.donors.report(id), queryFn: () => fetchDonorReport(id) });
}

/** Recent donations, for naming the donation an item came in. Empty for a role that may not read them. */
export function useDonations(
  params: PageParams,
  enabled = true,
): UseQueryResult<readonly DonationReadModel[]> {
  return useQuery({
    queryKey: qk.donations.list(params),
    queryFn: () => fetchDonations(params),
    enabled,
  });
}

export function useCreateDonor(): UseMutationResult<CreatedResource, Error, DonorRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: DonorRequest) => postCreate(DONORS, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.donors.all }),
  });
}

export function useUpdateDonor(id: string): UseMutationResult<void, Error, DonorRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: DonorRequest) => put204(donorPath(id), body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.donors.all });
      await queryClient.invalidateQueries({ queryKey: qk.donations.all });
    },
  });
}

/** Erasure: the donor's details are deleted; their donations, items and values stay. */
export function useEraseDonor(): UseMutationResult<void, Error, string> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => delete204(donorPath(id)),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.donors.all });
      await queryClient.invalidateQueries({ queryKey: qk.donations.all });
    },
  });
}

export function useCreateDonation(): UseMutationResult<
  CreatedResource,
  Error,
  CreateDonationRequest
> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateDonationRequest) => postCreate(DONATIONS, body),
    onSuccess: async (_created, body) => {
      await queryClient.invalidateQueries({ queryKey: qk.donations.all });
      await queryClient.invalidateQueries({ queryKey: qk.donors.donations(body.donorId) });
      await queryClient.invalidateQueries({ queryKey: qk.donors.report(body.donorId) });
    },
  });
}
