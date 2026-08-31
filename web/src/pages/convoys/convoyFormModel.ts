import { z } from 'zod';

import type {
  ConvoyReadModel,
  CreateConvoyRequest,
  UpdateConvoyRequest,
} from '../../api/schemas/convoys';

export interface ConvoyFormValues {
  start: string;
  expectedEnd: string;
}

export function emptyConvoyForm(): ConvoyFormValues {
  return { start: '', expectedEnd: '' };
}

export function convoyToFormValues(convoy: ConvoyReadModel): ConvoyFormValues {
  return { start: convoy.start.slice(0, 16), expectedEnd: convoy.expectedEnd.slice(0, 16) };
}

export function convoyFormToRequest(values: ConvoyFormValues): CreateConvoyRequest {
  return { start: values.start, expectedEnd: values.expectedEnd };
}

export function convoyFormToUpdateRequest(values: ConvoyFormValues): UpdateConvoyRequest {
  return convoyFormToRequest(values);
}

const localDateTime = (message: string) =>
  z.string().refine((raw) => raw.length > 0 && !Number.isNaN(Date.parse(raw)), message);

export const convoyFormSchema = z
  .object({
    start: localDateTime('Enter a start date and time'),
    expectedEnd: localDateTime('Enter an expected end date and time'),
  })
  .refine((values) => Date.parse(values.expectedEnd) > Date.parse(values.start), {
    message: "'Expected end' must be after 'Start'.",
    path: ['expectedEnd'],
  });
