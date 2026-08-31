import { z } from 'zod';

import type {
  AddBoxItemRequest,
  BoxReadModel,
  CreateBoxRequest,
  UpdateBoxRequest,
  ValidateBoxRequest,
} from '../../api/schemas/boxes';

// ---- Box create / edit -----------------------------------------------------

export interface BoxFormValues {
  receiverRef: string;
  house: string;
  street: string;
  city: string;
  country: string;
  postcode: string;
}

export function emptyBoxForm(): BoxFormValues {
  return { receiverRef: '', house: '', street: '', city: '', country: '', postcode: '' };
}

export function boxToFormValues(box: BoxReadModel): BoxFormValues {
  return {
    receiverRef: box.receiverRef ?? '',
    house: box.house ?? '',
    street: box.street ?? '',
    city: box.city ?? '',
    country: box.country ?? '',
    postcode: box.postcode ?? '',
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
  const house = trimmed(values.house);
  if (house !== undefined) request.house = house;
  const street = trimmed(values.street);
  if (street !== undefined) request.street = street;
  const city = trimmed(values.city);
  if (city !== undefined) request.city = city;
  const country = trimmed(values.country);
  if (country !== undefined) request.country = country;
  const postcode = trimmed(values.postcode);
  if (postcode !== undefined) request.postcode = postcode;
  return request;
}

export function boxFormToUpdateRequest(values: BoxFormValues): UpdateBoxRequest {
  return boxFormToRequest(values);
}

export const boxFormSchema = z.object({
  receiverRef: z.string().max(64, 'Receiver reference must be 64 characters or fewer'),
  house: z.string().max(100, 'House must be 100 characters or fewer'),
  street: z.string().max(200, 'Street must be 200 characters or fewer'),
  city: z.string().max(100, 'City must be 100 characters or fewer'),
  country: z.string().max(100, 'Country must be 100 characters or fewer'),
  postcode: z.string().max(20, 'Postcode must be 20 characters or fewer'),
});

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
}

export function emptyValidateForm(): ValidateFormValues {
  return { validatedByPersonId: '', weightKg: '' };
}

export function validateFormToRequest(values: ValidateFormValues): ValidateBoxRequest {
  return {
    validatedByPersonId: values.validatedByPersonId,
    weightKg: Number(values.weightKg),
  };
}

export const validateFormSchema = z.object({
  validatedByPersonId: z.string().min(1, 'Name the volunteer who checked the box'),
  weightKg: z.string().refine((raw) => {
    const n = Number(raw.trim());
    return raw.trim().length > 0 && Number.isInteger(n) && n >= 1 && n <= 500;
  }, "'Weight' must be a whole number between 1 and 500"),
});
