import { z } from 'zod';

import type {
  AddBoxItemRequest,
  AssignBoxBayRequest,
  BoxReadModel,
  CreateBoxRequest,
  ItemValueSource,
  UpdateBoxRequest,
  ValidateBoxRequest,
} from '../../api/schemas/boxes';

// ---- Box create / edit -----------------------------------------------------

export interface BoxFormValues {
  receiverRef: string;
  locationId: string;
}

export function emptyBoxForm(): BoxFormValues {
  return { receiverRef: '', locationId: '' };
}

export function boxToFormValues(box: BoxReadModel): BoxFormValues {
  return {
    receiverRef: box.receiverRef ?? '',
    locationId: box.locationId === null ? '' : String(box.locationId),
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

export const boxFormSchema = z.object({
  receiverRef: z.string().max(64, 'Receiver reference must be 64 characters or fewer'),
  locationId: z.string(),
});

// ---- Add an item ---------------------------------------------------------

export interface ItemPropertyRow {
  key: string;
  value: string;
}

export interface AddItemFormValues {
  description: string;
  categoryId: string;
  quantity: string;
  valueGbp: string;
  valueSource: '' | ItemValueSource;
  expiresOn: string;
  commodityCode: string;
  properties: ItemPropertyRow[];
}

export function emptyAddItemForm(): AddItemFormValues {
  return {
    description: '',
    categoryId: '',
    quantity: '',
    valueGbp: '',
    valueSource: '',
    expiresOn: '',
    commodityCode: '',
    properties: [],
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

  const request: AddBoxItemRequest = {
    description: values.description.trim(),
    properties,
    categoryId: Number(values.categoryId),
  };

  const quantity = trimmed(values.quantity);
  if (quantity !== undefined) request.quantity = Number(quantity);
  const valueGbp = trimmed(values.valueGbp);
  if (valueGbp !== undefined && values.valueSource !== '') {
    request.valueGbp = Number(valueGbp);
    request.valueSource = values.valueSource;
  }
  const expiresOn = trimmed(values.expiresOn);
  if (expiresOn !== undefined) request.expiresOn = expiresOn;
  const commodityCode = trimmed(values.commodityCode);
  if (commodityCode !== undefined) request.commodityCode = commodityCode;

  return request;
}

export const addItemFormSchema = z
  .object({
    description: z
      .string()
      .trim()
      .min(1, 'Describe the item')
      .max(400, 'Description must be 400 characters or fewer'),
    categoryId: z.string().min(1, 'Choose a category'),
    quantity: z
      .string()
      .refine(
        (raw) => raw.trim().length === 0 || (/^\d+$/.test(raw.trim()) && Number(raw.trim()) >= 1),
        'Quantity must be a whole number of 1 or more',
      ),
    valueGbp: z
      .string()
      .refine(
        (raw) => raw.trim().length === 0 || /^\d+(\.\d{1,2})?$/.test(raw.trim()),
        'Value must be an amount in pounds, with up to 2 decimal places',
      ),
    valueSource: z.enum(['', 'Donor', 'Estimate']),
    expiresOn: z.string(),
    commodityCode: z
      .string()
      .refine(
        (raw) => raw.trim().length === 0 || /^\d{6,10}$/.test(raw.trim()),
        'Commodity code must be 6 to 10 digits',
      ),
    properties: z
      .array(
        z.object({
          key: z.string().max(100, 'Property names must be 100 characters or fewer'),
          value: z.string(),
        }),
      )
      .max(50, 'An item may carry at most 50 properties'),
  })
  .superRefine((values, context) => {
    const hasValue = values.valueGbp.trim().length > 0;
    if (hasValue && values.valueSource === '') {
      context.addIssue({
        code: 'custom',
        path: ['valueSource'],
        message: 'Say whether the donor gave the value or it is an estimate',
      });
    }
    if (!hasValue && values.valueSource !== '') {
      context.addIssue({
        code: 'custom',
        path: ['valueGbp'],
        message: 'Enter the value this source describes',
      });
    }
  });

// ---- Validate a box ----------------------------------------------------

export interface ValidateFormValues {
  weightKg: string;
  widthCm: string;
  depthCm: string;
  heightCm: string;
}

export function emptyValidateForm(): ValidateFormValues {
  return { weightKg: '', widthCm: '', depthCm: '', heightCm: '' };
}

function decimalNumber(value: string): number | undefined {
  const t = value.trim();
  return t.length > 0 ? Number(t) : undefined;
}

export function validateFormToRequest(values: ValidateFormValues): ValidateBoxRequest {
  // Who vouched for the box is the caller's linked person, resolved by the API from the login.
  const request: ValidateBoxRequest = { weightKg: Number(values.weightKg) };

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
}

export function emptyAssignBayForm(): AssignBayFormValues {
  return { bayId: '' };
}

export function assignBayFormToRequest(values: AssignBayFormValues): AssignBoxBayRequest {
  return { bayId: Number(values.bayId) };
}

export const assignBayFormSchema = z.object({
  bayId: z.string().min(1, 'Choose a bay'),
});
