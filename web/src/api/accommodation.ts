import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';

import type { CreatedResource } from './client';
import { delete204, getJson, post204, postCreate, put204 } from './http';
import { qk } from './queryKeys';
import { accommodationCoverageSchema, accommodationSchema } from './schemas/accommodation';
import type {
  Accommodation,
  AccommodationBookingRequest,
  AccommodationCoverage,
} from './schemas/accommodation';

const basePath = (convoyId: number) => `/convoys/${String(convoyId)}/accommodation`;
const selfPath = (convoyId: number, routePointId: number, personId: string) =>
  `${basePath(convoyId)}/self/${String(routePointId)}/${personId}`;

export const fetchAccommodation = (convoyId: number): Promise<Accommodation> =>
  getJson(basePath(convoyId), accommodationSchema);

export const fetchAccommodationCoverage = (convoyId: number): Promise<AccommodationCoverage> =>
  getJson(`${basePath(convoyId)}/coverage`, accommodationCoverageSchema);

export function useAccommodation(convoyId: number): UseQueryResult<Accommodation> {
  return useQuery({
    queryKey: qk.accommodation.list(convoyId),
    queryFn: () => fetchAccommodation(convoyId),
  });
}

export function useAccommodationCoverage(convoyId: number): UseQueryResult<AccommodationCoverage> {
  return useQuery({
    queryKey: qk.accommodation.coverage(convoyId),
    queryFn: () => fetchAccommodationCoverage(convoyId),
  });
}

// Bookings, flags, the grid, the task list and the budget's hotel line all move together.
function useRefreshAccommodation(convoyId: number) {
  const queryClient = useQueryClient();
  return async () => {
    await queryClient.invalidateQueries({ queryKey: qk.accommodation.all(convoyId) });
    await queryClient.invalidateQueries({ queryKey: qk.declarations.tasks(convoyId) });
    await queryClient.invalidateQueries({ queryKey: qk.convoys.budget(convoyId) });
    await queryClient.invalidateQueries({ queryKey: qk.convoys.costs(convoyId) });
  };
}

export function useBookAccommodation(
  convoyId: number,
): UseMutationResult<CreatedResource, Error, AccommodationBookingRequest> {
  const refresh = useRefreshAccommodation(convoyId);
  return useMutation({
    mutationFn: (body: AccommodationBookingRequest) => postCreate(basePath(convoyId), body),
    onSuccess: refresh,
  });
}

export function useCancelBooking(convoyId: number): UseMutationResult<void, Error, number> {
  const refresh = useRefreshAccommodation(convoyId);
  return useMutation({
    mutationFn: (bookingId: number) => delete204(`${basePath(convoyId)}/${String(bookingId)}`),
    onSuccess: refresh,
  });
}

export function useMigrateBooking(
  convoyId: number,
): UseMutationResult<void, Error, { bookingId: number; fromPersonId: string; toPersonId: string }> {
  const refresh = useRefreshAccommodation(convoyId);
  return useMutation({
    mutationFn: ({ bookingId, fromPersonId, toPersonId }) =>
      post204(`${basePath(convoyId)}/${String(bookingId)}/migrate`, { fromPersonId, toPersonId }),
    onSuccess: refresh,
  });
}

export function useSetSelfAccommodation(
  convoyId: number,
): UseMutationResult<void, Error, { routePointId: number; personId: string; arranged: boolean }> {
  const refresh = useRefreshAccommodation(convoyId);
  return useMutation({
    mutationFn: ({ routePointId, personId, arranged }) =>
      arranged
        ? put204(selfPath(convoyId, routePointId, personId))
        : delete204(selfPath(convoyId, routePointId, personId)),
    onSuccess: refresh,
  });
}
