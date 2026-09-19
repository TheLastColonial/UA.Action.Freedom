import { z } from 'zod';

// An in-app path: one leading slash, and never `//` or `/\`, which a browser would read as
// another host. The router navigates within /app/ anyway, but the value round-trips through
// the identity provider, so it is validated as untrusted input on the way back.
const inAppPath = z
  .string()
  .regex(/^\/(?![/\\])/)
  .refine((path) => !path.includes('\\'));

const signInStateSchema = z.object({ returnTo: inAppPath });

export type SignInState = z.infer<typeof signInStateSchema>;

/** Where to take the user after signing in: the page they asked for, else the dashboard. */
export function returnPathFrom(state: unknown): string {
  const parsed = signInStateSchema.safeParse(state);
  return parsed.success ? parsed.data.returnTo : '/';
}
