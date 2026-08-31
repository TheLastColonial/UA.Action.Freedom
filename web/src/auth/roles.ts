import { z } from 'zod';

// The five application roles. Names match the Keycloak client roles and the Entra app roles
// they become in Azure — see docs/local-authentication.md and iac/tofu/keycloak.tf.
export const roleSchema = z.enum([
  'Administrator',
  'Purchaser',
  'Dispatcher',
  'Loader',
  'GroundOfficer',
]);

export type Role = z.infer<typeof roleSchema>;

export const ALL_ROLES: readonly Role[] = roleSchema.options;

// The token's `roles` claim is flat and multivalued, but a single role can arrive as a bare
// string. Anything unrecognised — a wrong shape, an unknown name — degrades to "no roles"
// rather than throwing, so a malformed token leaves the user with no policies, not a crash.
const rolesClaimSchema = z
  .union([roleSchema, z.array(z.unknown())])
  .transform((value) => (Array.isArray(value) ? value : [value]))
  .pipe(z.array(z.unknown()).transform((values) => values.filter(isRole)))
  .catch([]);

function isRole(value: unknown): value is Role {
  return roleSchema.safeParse(value).success;
}

export function parseRolesClaim(claim: unknown): readonly Role[] {
  return rolesClaimSchema.parse(claim);
}
