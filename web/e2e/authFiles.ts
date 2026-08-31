export const SEED_USERS = ['admin', 'operator', 'groundofficer'] as const;

export type SeedUser = (typeof SEED_USERS)[number];

/** Storage-state path produced by auth.setup.ts for a seed user. */
export function authFile(user: SeedUser): string {
  return `e2e/.auth/${user}.json`;
}
