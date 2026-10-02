import { z } from 'zod';

// Response shape — src/UA.Action.Freedom.Api/People/MeEndpoints.cs (MeResponse). The person id
// and display name are null until an Administrator links the login to a volunteer.
export const meSchema = z.object({
  subject: z.string().nullable(),
  roles: z.array(z.string()),
  personId: z.string().nullable(),
  displayName: z.string().nullable(),
});

export type Me = z.infer<typeof meSchema>;
