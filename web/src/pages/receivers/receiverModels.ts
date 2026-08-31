import { z } from 'zod';

import type { SetReceiverDetailRequest } from '../../api/receiverDetail';
import type {
  CreateReceiverRequest,
  ReceiverReadModel,
  UpdateReceiverRequest,
} from '../../api/schemas/receivers';

// ---- Receiver (organisation + region) --------------------------------

export interface ReceiverFormValues {
  organisation: string;
  region: string;
}

export function emptyReceiverForm(): ReceiverFormValues {
  return { organisation: '', region: '' };
}

export function receiverToFormValues(receiver: ReceiverReadModel): ReceiverFormValues {
  return { organisation: receiver.organisation, region: receiver.region };
}

export function receiverFormToRequest(values: ReceiverFormValues): CreateReceiverRequest {
  return { organisation: values.organisation.trim(), region: values.region.trim() };
}

export function receiverFormToUpdateRequest(values: ReceiverFormValues): UpdateReceiverRequest {
  return receiverFormToRequest(values);
}

export const receiverFormSchema = z.object({
  organisation: z
    .string()
    .trim()
    .min(1, 'Organisation is required')
    .max(200, 'Organisation must be 200 characters or fewer'),
  region: z
    .string()
    .trim()
    .min(1, 'Region is required')
    .max(100, 'Region must be 100 characters or fewer'),
});

// ---- Sensitive delivery detail (Ground Officer only) -----------------

export interface DetailFormValues {
  contactName: string;
  contactPhone: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  postCode: string;
}

export function emptyDetailForm(): DetailFormValues {
  return {
    contactName: '',
    contactPhone: '',
    addressLine1: '',
    addressLine2: '',
    city: '',
    postCode: '',
  };
}

function trimmedOptional(value: string): string | undefined {
  const t = value.trim();
  return t.length > 0 ? t : undefined;
}

export function detailFormToRequest(values: DetailFormValues): SetReceiverDetailRequest {
  const request: SetReceiverDetailRequest = {
    contactName: values.contactName.trim(),
    contactPhone: values.contactPhone.trim(),
    addressLine1: values.addressLine1.trim(),
    city: values.city.trim(),
  };
  const addressLine2 = trimmedOptional(values.addressLine2);
  if (addressLine2 !== undefined) request.addressLine2 = addressLine2;
  const postCode = trimmedOptional(values.postCode);
  if (postCode !== undefined) request.postCode = postCode;
  return request;
}

// Messages are static — they must never quote a submitted address or phone number.
export const detailFormSchema = z.object({
  contactName: z
    .string()
    .trim()
    .min(1, 'A contact name is required')
    .max(200, 'The contact name must be 200 characters or fewer'),
  contactPhone: z
    .string()
    .trim()
    .min(1, 'A contact phone number is required')
    .max(50, 'The phone number must be 50 characters or fewer'),
  addressLine1: z
    .string()
    .trim()
    .min(1, 'The first address line is required')
    .max(200, 'Address line 1 must be 200 characters or fewer'),
  addressLine2: z.string().max(200, 'Address line 2 must be 200 characters or fewer'),
  city: z
    .string()
    .trim()
    .min(1, 'A city is required')
    .max(100, 'The city must be 100 characters or fewer'),
  postCode: z.string().max(20, 'The postcode must be 20 characters or fewer'),
});
