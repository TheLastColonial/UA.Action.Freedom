import { z } from 'zod';

// Response shape — src/UA.Action.Freedom.Application/People/PersonReadModel.cs.
// This is personal data: never log a value from it.
export const personReadModelSchema = z.object({
  id: z.string(),
  firstName: z.string(),
  lastName: z.string(),
  dateOfBirth: z.string(),
  joined: z.string(),
  phone: z.string().nullable(),
  isDriver: z.boolean(),
  committed: z.boolean(),
});

export type PersonReadModel = z.infer<typeof personReadModelSchema>;

// Request shape — src/UA.Action.Freedom.Api/People/PersonRequests.cs.
export interface CreatePersonRequest {
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  joined: string;
  phone?: string;
  isDriver: boolean;
  committed: boolean;
}

export type UpdatePersonRequest = CreatePersonRequest;
