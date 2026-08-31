import { z } from 'zod';

// Runtime configuration, parsed once at startup. Empty base URL means "same origin" — the
// SPA is served by the API host in the container, and proxied to it by the Vite dev server.
const envSchema = z.object({
  VITE_API_BASE_URL: z.string().default(''),
  VITE_OIDC_AUTHORITY: z.string().default('http://localhost:8081/realms/freedom'),
  VITE_OIDC_CLIENT_ID: z.string().default('freedom-spa'),
});

export const env = envSchema.parse(import.meta.env);
