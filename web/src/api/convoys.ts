import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import type { CreatedResource, ParentMissing } from './client';
import { delete204, getCollection, getJson, postCreate, postTransition, put204 } from './http';
import { ApiNotFound } from './problem';
import { qk } from './queryKeys';
import type { PageParams } from './queryKeys';
import { manifestBoxReadModelSchema } from './schemas/manifests';
import type { CreateConvoyVehicleManifestRequest, ManifestBoxReadModel } from './schemas/manifests';
import {
  convoyReadModelSchema,
  convoyReadinessReadModelSchema,
  convoyVehicleReadModelSchema,
  ferryBookingReadModelSchema,
  routeStopReadModelSchema,
  vehicleCrewReadModelSchema,
  vehicleInsuranceReadModelSchema,
} from './schemas/convoys';
import type {
  ConvoyReadModel,
  ConvoyReadinessReadModel,
  ConvoyVehicleReadModel,
  CreateConvoyRequest,
  CrewRole,
  FerryBookingReadModel,
  RecordFerryBookingRequest,
  RecordInsuranceRequest,
  ReplaceConvoyRouteRequest,
  RouteStopReadModel,
  UpdateConvoyRequest,
  VehicleCrewReadModel,
  VehicleInsuranceReadModel,
} from './schemas/convoys';

const BASE = '/convoys';
const idPath = (id: number) => `${BASE}/${String(id)}`;
const vinPath = (id: number, vin: string) => `${idPath(id)}/vehicles/${encodeURIComponent(vin)}`;

export function fetchConvoys(params: PageParams): Promise<readonly ConvoyReadModel[]> {
  return getJson(BASE, z.array(convoyReadModelSchema), {
    page: params.page,
    pageSize: params.pageSize,
  });
}

export function fetchConvoy(id: number): Promise<ConvoyReadModel> {
  return getJson(idPath(id), convoyReadModelSchema);
}

export function createConvoy(body: CreateConvoyRequest): Promise<CreatedResource> {
  return postCreate(BASE, body);
}

export function updateConvoy(id: number, body: UpdateConvoyRequest): Promise<void> {
  return put204(idPath(id), body);
}

export function deleteConvoy(id: number): Promise<void> {
  return delete204(idPath(id));
}

export function fetchConvoyRoute(
  id: number,
): Promise<readonly RouteStopReadModel[] | ParentMissing> {
  return getCollection(`${idPath(id)}/route`, routeStopReadModelSchema);
}

export function replaceConvoyRoute(id: number, body: ReplaceConvoyRouteRequest): Promise<void> {
  return put204(`${idPath(id)}/route`, body);
}

export function fetchConvoyVehicles(
  id: number,
): Promise<readonly ConvoyVehicleReadModel[] | ParentMissing> {
  return getCollection(`${idPath(id)}/vehicles`, convoyVehicleReadModelSchema);
}

export function assignVehicle(id: number, vin: string): Promise<void> {
  return put204(vinPath(id, vin));
}

// The registered receiver a vehicle is handed over to in Ukraine. The API refuses one that is not
// registered (409) or does not exist (422).
export function setHandoverReceiver(id: number, vin: string, receiverRef: string): Promise<void> {
  return put204(`${vinPath(id, vin)}/handover-receiver`, { receiverRef });
}

// Before the truck list is published this takes the vehicle off it; afterwards it records that
// the vehicle left the convoy, keeping the entry, its crew, its insurance and its manifest.
export function unassignVehicle(id: number, vin: string, reason?: string): Promise<void> {
  const query = reason === undefined ? '' : `?reason=${encodeURIComponent(reason)}`;
  return delete204(`${vinPath(id, vin)}${query}`);
}

export function fetchVehicleCrew(
  id: number,
  vin: string,
): Promise<readonly VehicleCrewReadModel[] | ParentMissing> {
  return getCollection(`${vinPath(id, vin)}/crew`, vehicleCrewReadModelSchema);
}

export interface CrewAssignment {
  personId: string;
  role: CrewRole;
}

export function assignCrew(
  id: number,
  vin: string,
  { personId, role }: CrewAssignment,
): Promise<void> {
  return put204(`${vinPath(id, vin)}/crew/${encodeURIComponent(personId)}`, { role });
}

export interface CrewRemoval {
  personId: string;
}

export function unassignCrew(id: number, vin: string, { personId }: CrewRemoval): Promise<void> {
  return delete204(`${vinPath(id, vin)}/crew/${encodeURIComponent(personId)}`);
}

// A manifest is the paperwork for one vehicle on one convoy, so it is opened here rather than
// at POST /manifests — which no longer exists.
export function createManifestForVehicle(
  id: number,
  vin: string,
  body: CreateConvoyVehicleManifestRequest,
): Promise<CreatedResource> {
  return postCreate(`${vinPath(id, vin)}/manifest`, body);
}

// A vehicle with no insurance recorded is a bare 404; that is an answer ("not recorded"), not
// an error, so it resolves to null.
export async function fetchInsurance(
  id: number,
  vin: string,
): Promise<VehicleInsuranceReadModel | null> {
  try {
    return await getJson(`${vinPath(id, vin)}/insurance`, vehicleInsuranceReadModelSchema);
  } catch (error) {
    if (error instanceof ApiNotFound) {
      return null;
    }
    throw error;
  }
}

export function recordInsurance(
  id: number,
  vin: string,
  body: RecordInsuranceRequest,
): Promise<void> {
  return put204(`${vinPath(id, vin)}/insurance`, body);
}

export function fetchVehicleBoxes(
  id: number,
  vin: string,
): Promise<readonly ManifestBoxReadModel[] | ParentMissing> {
  return getCollection(`${vinPath(id, vin)}/boxes`, manifestBoxReadModelSchema);
}

// Putting a box on a second vehicle moves it: a box is on at most one truck-list entry.
export function allocateBox(id: number, vin: string, boxId: number): Promise<void> {
  return put204(`${vinPath(id, vin)}/boxes/${String(boxId)}`);
}

export function removeBoxAllocation(id: number, vin: string, boxId: number): Promise<void> {
  return delete204(`${vinPath(id, vin)}/boxes/${String(boxId)}`);
}

// A vehicle with no ferry booked is a bare 404; that is an answer ("not booked"), not an error.
export async function fetchFerryBooking(
  id: number,
  vin: string,
): Promise<FerryBookingReadModel | null> {
  try {
    return await getJson(`${vinPath(id, vin)}/ferry`, ferryBookingReadModelSchema);
  } catch (error) {
    if (error instanceof ApiNotFound) {
      return null;
    }
    throw error;
  }
}

export function recordFerryBooking(
  id: number,
  vin: string,
  body: RecordFerryBookingRequest,
): Promise<void> {
  return put204(`${vinPath(id, vin)}/ferry`, body);
}

export function cancelFerryBooking(id: number, vin: string): Promise<void> {
  return delete204(`${vinPath(id, vin)}/ferry`);
}

export function arriveConvoy(id: number): Promise<void> {
  return postTransition(`${idPath(id)}/arrive`);
}

export function publishTruckList(id: number): Promise<void> {
  return postTransition(`${idPath(id)}/publish-truck-list`);
}

export function useConvoys(params: PageParams): UseQueryResult<readonly ConvoyReadModel[]> {
  return useQuery({ queryKey: qk.convoys.list(params), queryFn: () => fetchConvoys(params) });
}

export function useConvoy(
  id: number,
  options: { enabled?: boolean } = {},
): UseQueryResult<ConvoyReadModel> {
  return useQuery({
    queryKey: qk.convoys.detail(id),
    queryFn: () => fetchConvoy(id),
    enabled: options.enabled ?? true,
  });
}

export function useConvoyRoute(
  id: number,
): UseQueryResult<readonly RouteStopReadModel[] | ParentMissing> {
  return useQuery({ queryKey: qk.convoys.route(id), queryFn: () => fetchConvoyRoute(id) });
}

export function useConvoyVehicles(
  id: number,
): UseQueryResult<readonly ConvoyVehicleReadModel[] | ParentMissing> {
  return useQuery({ queryKey: qk.convoys.vehicles(id), queryFn: () => fetchConvoyVehicles(id) });
}

export function useCreateConvoy(): UseMutationResult<CreatedResource, Error, CreateConvoyRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createConvoy,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.all }),
  });
}

export function useUpdateConvoy(id: number): UseMutationResult<void, Error, UpdateConvoyRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateConvoyRequest) => updateConvoy(id, body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.convoys.all });
      await queryClient.invalidateQueries({ queryKey: qk.convoys.detail(id) });
    },
  });
}

export function useDeleteConvoy(): UseMutationResult<void, Error, number> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: deleteConvoy,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.all }),
  });
}

export function useReplaceConvoyRoute(
  id: number,
): UseMutationResult<void, Error, ReplaceConvoyRouteRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: ReplaceConvoyRouteRequest) => replaceConvoyRoute(id, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.route(id) }),
  });
}

export function useAssignVehicle(id: number): UseMutationResult<void, Error, string> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (vin: string) => assignVehicle(id, vin),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.vehicles(id) }),
  });
}

export interface HandoverReceiverChoice {
  vin: string;
  receiverRef: string;
}

export function useSetHandoverReceiver(
  id: number,
): UseMutationResult<void, Error, HandoverReceiverChoice> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ vin, receiverRef }: HandoverReceiverChoice) =>
      setHandoverReceiver(id, vin, receiverRef),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.vehicles(id) }),
  });
}

/** A reason is only meaningful once the truck list is published, when a removal is a withdrawal. */
export interface VehicleRemoval {
  vin: string;
  reason?: string;
}

export function useUnassignVehicle(id: number): UseMutationResult<void, Error, VehicleRemoval> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ vin, reason }: VehicleRemoval) => unassignVehicle(id, vin, reason),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.convoys.vehicles(id) });
      await queryClient.invalidateQueries({ queryKey: qk.convoys.readiness(id) });
    },
  });
}

export function useVehicleCrew(
  id: number,
  vin: string,
): UseQueryResult<readonly VehicleCrewReadModel[] | ParentMissing> {
  return useQuery({
    queryKey: qk.convoys.vehicleCrew(id, vin),
    queryFn: () => fetchVehicleCrew(id, vin),
  });
}

// The insurance names the crew, so any change to it voids the policy — which is why every crew
// mutation invalidates the insurance as well as the crew.
export function useAssignCrew(
  id: number,
  vin: string,
): UseMutationResult<void, Error, CrewAssignment> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (assignment: CrewAssignment) => assignCrew(id, vin, assignment),
    onSuccess: () => invalidateCrew(queryClient, id, vin),
  });
}

export function useUnassignCrew(
  id: number,
  vin: string,
): UseMutationResult<void, Error, CrewRemoval> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (removal: CrewRemoval) => unassignCrew(id, vin, removal),
    onSuccess: () => invalidateCrew(queryClient, id, vin),
  });
}

async function invalidateCrew(
  queryClient: ReturnType<typeof useQueryClient>,
  id: number,
  vin: string,
): Promise<void> {
  await queryClient.invalidateQueries({ queryKey: qk.convoys.vehicleCrew(id, vin) });
  await queryClient.invalidateQueries({ queryKey: qk.convoys.vehicles(id) });
  await queryClient.invalidateQueries({ queryKey: qk.convoys.insurance(id, vin) });
  await queryClient.invalidateQueries({ queryKey: qk.convoys.readiness(id) });
}

export function useCreateManifestForVehicle(
  id: number,
  vin: string,
): UseMutationResult<CreatedResource, Error, CreateConvoyVehicleManifestRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateConvoyVehicleManifestRequest) =>
      createManifestForVehicle(id, vin, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.manifests.all }),
  });
}

export function usePublishTruckList(id: number): UseMutationResult<void, Error, void> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => publishTruckList(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.convoys.detail(id) });
      await queryClient.invalidateQueries({ queryKey: qk.convoys.vehicles(id) });
      // A published truck list is a precondition for proposing a manifest against the convoy.
      await queryClient.invalidateQueries({ queryKey: qk.manifests.all });
    },
  });
}

export function useVehicleInsurance(
  id: number,
  vin: string,
): UseQueryResult<VehicleInsuranceReadModel | null> {
  return useQuery({
    queryKey: qk.convoys.insurance(id, vin),
    queryFn: () => fetchInsurance(id, vin),
  });
}

export function useRecordInsurance(
  id: number,
  vin: string,
): UseMutationResult<void, Error, RecordInsuranceRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: RecordInsuranceRequest) => recordInsurance(id, vin, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.insurance(id, vin) }),
  });
}

export function useArriveConvoy(id: number): UseMutationResult<void, Error, void> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => arriveConvoy(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: qk.convoys.all });
      await queryClient.invalidateQueries({ queryKey: qk.vehicles.all });
    },
  });
}

// Readiness is derived from the crew, the insurance and the route, all edited on other tabs, so
// it is fetched afresh whenever it is shown rather than trusted from the cache.
export function useConvoyReadiness(id: number): UseQueryResult<ConvoyReadinessReadModel> {
  return useQuery({
    queryKey: qk.convoys.readiness(id),
    queryFn: () => getJson(`${idPath(id)}/readiness`, convoyReadinessReadModelSchema),
    refetchOnMount: 'always',
  });
}

export function useVehicleBoxes(
  id: number,
  vin: string,
): UseQueryResult<readonly ManifestBoxReadModel[] | ParentMissing> {
  return useQuery({
    queryKey: qk.convoys.vehicleBoxes(id, vin),
    queryFn: () => fetchVehicleBoxes(id, vin),
  });
}

// A move changes two vehicles' cargo, so every vehicle on the convoy is refreshed, and the
// manifest's read-through (its cargo and its weight) with them.
async function invalidateCargo(
  queryClient: ReturnType<typeof useQueryClient>,
  id: number,
): Promise<void> {
  await queryClient.invalidateQueries({ queryKey: qk.convoys.vehicles(id) });
  await queryClient.invalidateQueries({ queryKey: qk.manifests.all });
}

export function useAllocateBox(id: number, vin: string): UseMutationResult<void, Error, number> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (boxId: number) => allocateBox(id, vin, boxId),
    onSuccess: () => invalidateCargo(queryClient, id),
  });
}

export function useRemoveBoxAllocation(
  id: number,
  vin: string,
): UseMutationResult<void, Error, number> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (boxId: number) => removeBoxAllocation(id, vin, boxId),
    onSuccess: () => invalidateCargo(queryClient, id),
  });
}

export function useFerryBooking(
  id: number,
  vin: string,
): UseQueryResult<FerryBookingReadModel | null> {
  return useQuery({
    queryKey: qk.convoys.ferry(id, vin),
    queryFn: () => fetchFerryBooking(id, vin),
  });
}

export function useRecordFerryBooking(
  id: number,
  vin: string,
): UseMutationResult<void, Error, RecordFerryBookingRequest> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: RecordFerryBookingRequest) => recordFerryBooking(id, vin, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.ferry(id, vin) }),
  });
}

export function useCancelFerryBooking(
  id: number,
  vin: string,
): UseMutationResult<void, Error, void> {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => cancelFerryBooking(id, vin),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.convoys.ferry(id, vin) }),
  });
}
