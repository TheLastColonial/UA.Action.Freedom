import { z } from 'zod';

import type {
  CreateConvoyVehicleManifestRequest,
  ManifestReadModel,
  UpdateManifestRequest,
} from '../../api/schemas/manifests';

// ---- Manifest create / edit ---------------------------------------------
//
// The convoy and the vehicle are absent. They are the truck-list entry the manifest is the
// paperwork for — its identity, not attributes of it — so creation takes them from the route
// (POST /convoys/{id}/vehicles/{vin}/manifest) and an edit cannot reach them at all.

export interface ManifestFormValues {
  id: string;
  deliveryNotes: string;
  ferryBookingComplete: boolean;
}

export function emptyManifestForm(): ManifestFormValues {
  return { id: '', deliveryNotes: '', ferryBookingComplete: false };
}

export function manifestToFormValues(manifest: ManifestReadModel): ManifestFormValues {
  return {
    id: manifest.id,
    deliveryNotes: manifest.deliveryNotes ?? '',
    ferryBookingComplete: manifest.ferryBookingComplete,
  };
}

function trimmed(value: string): string | undefined {
  const t = value.trim();
  return t.length > 0 ? t : undefined;
}

function baseRequest(values: ManifestFormValues): UpdateManifestRequest {
  const request: UpdateManifestRequest = { ferryBookingComplete: values.ferryBookingComplete };
  const notes = trimmed(values.deliveryNotes);
  if (notes !== undefined) request.deliveryNotes = notes;
  return request;
}

export function manifestFormToRequest(
  values: ManifestFormValues,
): CreateConvoyVehicleManifestRequest {
  return { id: values.id.trim(), ...baseRequest(values) };
}

export function manifestFormToUpdateRequest(values: ManifestFormValues): UpdateManifestRequest {
  return baseRequest(values);
}

export const manifestFormSchema = z.object({
  id: z
    .string()
    .trim()
    .min(1, 'A manifest reference is required')
    .max(32, 'The reference must be 32 characters or fewer'),
  deliveryNotes: z.string().max(2000, 'Delivery notes must be 2000 characters or fewer'),
  ferryBookingComplete: z.boolean(),
});
