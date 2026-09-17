import { z } from 'zod';

import type {
  AddBoxItemRequest,
  AssignBoxBayRequest,
  BoxItemReadModel,
  BoxReadModel,
  CreateBoxRequest,
  UpdateBoxRequest,
  ValidateBoxRequest,
} from '../../api/schemas/boxes';

// ---- Box create / edit -----------------------------------------------------

export interface BoxFormValues {
  receiverRef: string;
  locationId: string;
  // Loader-only, create-time convenience: place the box straight in a bay. Never sent as part
  // of the box request itself — see boxFormToRequest — the create page follows up with the
  // ordinary PUT /boxes/{id}/bay call once the box exists.
  bayId: string;
  assignedByPersonId: string;
}

export function emptyBoxForm(): BoxFormValues {
  return { receiverRef: '', locationId: '', bayId: '', assignedByPersonId: '' };
}

export function boxToFormValues(box: BoxReadModel): BoxFormValues {
  return {
    receiverRef: box.receiverRef ?? '',
    locationId: box.locationId === null ? '' : String(box.locationId),
    bayId: '',
    assignedByPersonId: '',
  };
}

function trimmed(value: string): string | undefined {
  const t = value.trim();
  return t.length > 0 ? t : undefined;
}

export function boxFormToRequest(values: BoxFormValues): CreateBoxRequest {
  const request: CreateBoxRequest = {};
  const receiverRef = trimmed(values.receiverRef);
  if (receiverRef !== undefined) request.receiverRef = receiverRef;
  const locationId = trimmed(values.locationId);
  if (locationId !== undefined) request.locationId = Number(locationId);
  return request;
}

export function boxFormToUpdateRequest(values: BoxFormValues): UpdateBoxRequest {
  return boxFormToRequest(values);
}

export const boxFormSchema = z
  .object({
    receiverRef: z.string().max(64, 'Receiver reference must be 64 characters or fewer'),
    locationId: z.string(),
    bayId: z.string(),
    assignedByPersonId: z.string(),
  })
  .refine(
    (values) => values.bayId.trim().length === 0 || values.assignedByPersonId.trim().length > 0,
    {
      message: 'Name the volunteer placing the box',
      path: ['assignedByPersonId'],
    },
  );

// ---- Add an item ---------------------------------------------------------

export interface ItemPropertyRow {
  key: string;
  value: string;
}

export interface AddItemFormValues {
  description: string;
  properties: ItemPropertyRow[];
}

export function emptyAddItemForm(): AddItemFormValues {
  return { description: '', properties: [] };
}

export function itemToFormValues(item: BoxItemReadModel): AddItemFormValues {
  return {
    description: item.description,
    properties: Object.entries(item.properties).map(([key, value]) => ({ key, value })),
  };
}

export function addItemFormToRequest(values: AddItemFormValues): AddBoxItemRequest {
  const properties: Record<string, string> = {};
  for (const row of values.properties) {
    const key = row.key.trim();
    if (key.length > 0) {
      properties[key] = row.value.trim();
    }
  }
  return { description: values.description.trim(), properties };
}

export const addItemFormSchema = z.object({
  description: z
    .string()
    .trim()
    .min(1, 'Describe the item')
    .max(400, 'Description must be 400 characters or fewer'),
  properties: z
    .array(
      z.object({
        key: z.string().max(100, 'Property names must be 100 characters or fewer'),
        value: z.string(),
      }),
    )
    .max(50, 'An item may carry at most 50 properties'),
});

// ---- Validate a box ----------------------------------------------------

export interface ValidateFormValues {
  validatedByPersonId: string;
  weightKg: string;
  widthCm: string;
  depthCm: string;
  heightCm: string;
}

export function emptyValidateForm(): ValidateFormValues {
  return { validatedByPersonId: '', weightKg: '', widthCm: '', depthCm: '', heightCm: '' };
}

function decimalNumber(value: string): number | undefined {
  const t = value.trim();
  return t.length > 0 ? Number(t) : undefined;
}

export function validateFormToRequest(values: ValidateFormValues): ValidateBoxRequest {
  const request: ValidateBoxRequest = {
    validatedByPersonId: values.validatedByPersonId,
    weightKg: Number(values.weightKg),
  };

  const widthCm = decimalNumber(values.widthCm);
  if (widthCm !== undefined) request.widthCm = widthCm;
  const depthCm = decimalNumber(values.depthCm);
  if (depthCm !== undefined) request.depthCm = depthCm;
  const heightCm = decimalNumber(values.heightCm);
  if (heightCm !== undefined) request.heightCm = heightCm;

  return request;
}

// A dimension a volunteer can carry. Like the 1..500 weight bound, a typo guard rather than a
// real bound — mirrors ValidateBoxRequestValidator.MaxBoxDimensionCm.
const optionalNonNegativeDecimal = (message: string) =>
  z.string().refine((raw) => {
    const t = raw.trim();
    if (t.length === 0) return true;
    return /^\d+(\.\d{1,2})?$/.test(t) && Number(t) >= 1 && Number(t) <= 1000;
  }, message);

export const validateFormSchema = z.object({
  validatedByPersonId: z.string().min(1, 'Name the volunteer who checked the box'),
  weightKg: z.string().refine((raw) => {
    const n = Number(raw.trim());
    return raw.trim().length > 0 && Number.isInteger(n) && n >= 1 && n <= 500;
  }, "'Weight' must be a whole number between 1 and 500"),
  widthCm: optionalNonNegativeDecimal(
    "'Width' must be a number between 1 and 1000, with up to 2 decimal places",
  ),
  depthCm: optionalNonNegativeDecimal(
    "'Depth' must be a number between 1 and 1000, with up to 2 decimal places",
  ),
  heightCm: optionalNonNegativeDecimal(
    "'Height' must be a number between 1 and 1000, with up to 2 decimal places",
  ),
});

// ---- Assign a bay -----------------------------------------------------

export interface AssignBayFormValues {
  bayId: string;
  assignedByPersonId: string;
}

export function emptyAssignBayForm(): AssignBayFormValues {
  return { bayId: '', assignedByPersonId: '' };
}

export function assignBayFormToRequest(values: AssignBayFormValues): AssignBoxBayRequest {
  return { bayId: Number(values.bayId), assignedByPersonId: values.assignedByPersonId };
}

export const assignBayFormSchema = z.object({
  bayId: z.string().min(1, 'Choose a bay'),
  assignedByPersonId: z.string().min(1, 'Name the volunteer placing the box'),
});
