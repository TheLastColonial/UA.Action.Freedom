import { z } from 'zod';

import type {
  CreatePersonRequest,
  PersonReadModel,
  UpdatePersonRequest,
} from '../../api/schemas/people';

export interface PersonFormValues {
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  joined: string;
  phone: string;
  isDriver: boolean;
  committed: boolean;
}

export function emptyPersonForm(): PersonFormValues {
  return {
    firstName: '',
    lastName: '',
    dateOfBirth: '',
    joined: new Date().toISOString().slice(0, 10),
    phone: '',
    isDriver: false,
    committed: false,
  };
}

export function personToFormValues(person: PersonReadModel): PersonFormValues {
  return {
    firstName: person.firstName,
    lastName: person.lastName,
    dateOfBirth: person.dateOfBirth.slice(0, 10),
    joined: person.joined.slice(0, 10),
    phone: person.phone ?? '',
    isDriver: person.isDriver,
    committed: person.committed,
  };
}

export function personFormToRequest(values: PersonFormValues): CreatePersonRequest {
  const request: CreatePersonRequest = {
    firstName: values.firstName.trim(),
    lastName: values.lastName.trim(),
    dateOfBirth: values.dateOfBirth,
    joined: values.joined,
    isDriver: values.isDriver,
    // A volunteer who does not drive cannot be committed — mirror the server rule.
    committed: values.committed && values.isDriver,
  };
  const phone = values.phone.trim();
  if (phone.length > 0) {
    request.phone = phone;
  }
  return request;
}

export function personFormToUpdateRequest(values: PersonFormValues): UpdatePersonRequest {
  return personFormToRequest(values);
}

const calendarDate = (message: string) =>
  z.string().refine((raw) => {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(raw)) {
      return false;
    }
    const time = Date.parse(`${raw}T00:00:00Z`);
    return !Number.isNaN(time) && new Date(time).getUTCFullYear() > 1900;
  }, message);

export const personFormSchema = z
  .object({
    firstName: z
      .string()
      .trim()
      .min(1, 'First name is required')
      .max(100, 'First name must be 100 characters or fewer'),
    lastName: z
      .string()
      .trim()
      .min(1, 'Last name is required')
      .max(100, 'Last name must be 100 characters or fewer'),
    dateOfBirth: calendarDate('Enter a valid date of birth'),
    joined: calendarDate('Enter a valid joining date'),
    phone: z.string().max(50, 'Phone must be 50 characters or fewer'),
    isDriver: z.boolean(),
    committed: z.boolean(),
  })
  .refine((values) => !values.committed || values.isDriver, {
    message: "'Committed' can only be set for a volunteer who drives.",
    path: ['committed'],
  });
