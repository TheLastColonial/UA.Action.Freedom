import { z } from 'zod';

import type {
  CreateDonationRequest,
  DonorReadModel,
  DonorRequest,
} from '../../api/schemas/donations';

export interface DonorFormValues {
  name: string;
  email: string;
  phone: string;
}

export function emptyDonorForm(): DonorFormValues {
  return { name: '', email: '', phone: '' };
}

export function donorToFormValues(donor: DonorReadModel): DonorFormValues {
  return { name: donor.name, email: donor.email ?? '', phone: donor.phone ?? '' };
}

export function donorFormToRequest(values: DonorFormValues): DonorRequest {
  const request: DonorRequest = { name: values.name.trim() };
  const email = values.email.trim();
  if (email.length > 0) {
    request.email = email;
  }
  const phone = values.phone.trim();
  if (phone.length > 0) {
    request.phone = phone;
  }
  return request;
}

export const donorFormSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Name is required')
    .max(200, 'Name must be 200 characters or fewer'),
  email: z
    .string()
    .trim()
    .max(254, 'Email must be 254 characters or fewer')
    .refine(
      (raw) => raw.length === 0 || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(raw),
      'Enter a valid email address',
    ),
  phone: z.string().max(50, 'Phone must be 50 characters or fewer'),
});

export interface DonationFormValues {
  receivedOn: string;
  notes: string;
}

export function emptyDonationForm(): DonationFormValues {
  return { receivedOn: new Date().toISOString().slice(0, 10), notes: '' };
}

export function donationFormToRequest(
  donorId: string,
  values: DonationFormValues,
): CreateDonationRequest {
  const request: CreateDonationRequest = { donorId, receivedOn: values.receivedOn };
  const notes = values.notes.trim();
  if (notes.length > 0) {
    request.notes = notes;
  }
  return request;
}

export const donationFormSchema = z.object({
  receivedOn: z.string().refine((raw) => {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(raw)) {
      return false;
    }
    return !Number.isNaN(Date.parse(`${raw}T00:00:00Z`));
  }, 'Enter the date the donation was received'),
  notes: z.string().max(1000, 'Notes must be 1000 characters or fewer'),
});
