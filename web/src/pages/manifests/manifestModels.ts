import { z } from 'zod';

import type {
  CreateManifestRequest,
  ManifestReadModel,
  SetManifestTeamRequest,
  UpdateManifestRequest,
} from '../../api/schemas/manifests';

// ---- Manifest create / edit ---------------------------------------------

export interface ManifestFormValues {
  id: string;
  vin: string;
  convoyId: string;
  deliveryNotes: string;
  ferryBookingComplete: boolean;
}

export function emptyManifestForm(): ManifestFormValues {
  return { id: '', vin: '', convoyId: '', deliveryNotes: '', ferryBookingComplete: false };
}

export function manifestToFormValues(manifest: ManifestReadModel): ManifestFormValues {
  return {
    id: manifest.id,
    vin: manifest.vin ?? '',
    convoyId: manifest.convoyId === null ? '' : String(manifest.convoyId),
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
  const vin = trimmed(values.vin);
  if (vin !== undefined) request.vin = vin;
  const notes = trimmed(values.deliveryNotes);
  if (notes !== undefined) request.deliveryNotes = notes;
  const convoyId = trimmed(values.convoyId);
  if (convoyId !== undefined) request.convoyId = Number(convoyId);
  return request;
}

export function manifestFormToRequest(values: ManifestFormValues): CreateManifestRequest {
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
  vin: z.string().max(32, 'VIN must be 32 characters or fewer'),
  convoyId: z.string().refine((raw) => {
    if (raw.trim().length === 0) return true;
    const n = Number(raw.trim());
    return Number.isInteger(n) && n > 0;
  }, 'Convoy must be a whole number'),
  deliveryNotes: z.string().max(2000, 'Delivery notes must be 2000 characters or fewer'),
  ferryBookingComplete: z.boolean(),
});

// ---- Driver team for one leg ------------------------------------------

export interface TeamFormValues {
  primaryPersonId: string;
  secondaryPersonId: string;
}

export function emptyTeamForm(): TeamFormValues {
  return { primaryPersonId: '', secondaryPersonId: '' };
}

export function teamFormToRequest(values: TeamFormValues): SetManifestTeamRequest {
  const request: SetManifestTeamRequest = { primaryPersonId: values.primaryPersonId };
  const secondary = values.secondaryPersonId.trim();
  if (secondary.length > 0) {
    request.secondaryPersonId = secondary;
  }
  return request;
}

export const teamFormSchema = z
  .object({
    primaryPersonId: z.string().min(1, 'Name the volunteer leading this leg'),
    secondaryPersonId: z.string(),
  })
  .refine(
    (values) =>
      values.secondaryPersonId.length === 0 || values.secondaryPersonId !== values.primaryPersonId,
    {
      message: 'A driver team is two people — the same volunteer cannot crew both halves.',
      path: ['secondaryPersonId'],
    },
  );
