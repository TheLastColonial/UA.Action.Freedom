import { z } from 'zod';

import type {
  CreateBayRequest,
  CreateLocationRequest,
  LocationReadModel,
  UpdateLocationRequest,
} from '../../api/schemas/locations';

// ---- Location create / edit -------------------------------------------------

export interface LocationFormValues {
  name: string;
  house: string;
  street: string;
  city: string;
  country: string;
  postcode: string;
}

export function emptyLocationForm(): LocationFormValues {
  return { name: '', house: '', street: '', city: '', country: '', postcode: '' };
}

export function locationToFormValues(location: LocationReadModel): LocationFormValues {
  return {
    name: location.name,
    house: location.house ?? '',
    street: location.street ?? '',
    city: location.city ?? '',
    country: location.country ?? '',
    postcode: location.postcode ?? '',
  };
}

function trimmed(value: string): string | undefined {
  const t = value.trim();
  return t.length > 0 ? t : undefined;
}

export function locationFormToRequest(values: LocationFormValues): CreateLocationRequest {
  const request: CreateLocationRequest = { name: values.name.trim() };
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

export function locationFormToUpdateRequest(values: LocationFormValues): UpdateLocationRequest {
  return locationFormToRequest(values);
}

export const locationFormSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Name is required')
    .max(200, 'Name must be 200 characters or fewer'),
  house: z.string().max(100, 'House must be 100 characters or fewer'),
  street: z.string().max(200, 'Street must be 200 characters or fewer'),
  city: z.string().max(100, 'City must be 100 characters or fewer'),
  country: z.string().max(100, 'Country must be 100 characters or fewer'),
  postcode: z.string().max(20, 'Postcode must be 20 characters or fewer'),
});

// ---- Add a bay --------------------------------------------------------------

export interface BayFormValues {
  code: string;
}

export function emptyBayForm(): BayFormValues {
  return { code: '' };
}

export function bayFormToRequest(values: BayFormValues): CreateBayRequest {
  return { code: values.code.trim() };
}

export const bayFormSchema = z.object({
  code: z
    .string()
    .trim()
    .min(1, 'Give the bay a code')
    .max(20, 'Code must be 20 characters or fewer'),
});
