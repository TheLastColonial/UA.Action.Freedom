import { z } from 'zod';

// The application roles. Names match the Keycloak client roles and the Entra app roles they become
// in Azure — see docs/local-authentication.md and iac/tofu/keycloak.tf. ConvoyLeader is the exception:
// it is derived by the API from an open convoy-leader assignment (ADR 0010), never issued by the
// identity provider, so a token never carries it. GET /me reports ledConvoyIds, which the leader screens (plan 18) will turn into this role.
export const roleSchema = z.enum([
  'Administrator',
  'Purchaser',
  'Dispatcher',
  'Loader',
  'Mechanic',
  'GroundOfficer',
  'ConvoyLeader',
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
