import { z } from 'zod';

// RFC 7807. The API sends `application/problem+json` for both 400 validation failures (with
// an `errors` map keyed by PascalCase field name) and domain-rule failures (with a human
// `detail`).
export const problemJsonSchema = z.looseObject({
  type: z.string().optional(),
  title: z.string().optional(),
  status: z.number().optional(),
  detail: z.string().optional(),
  errors: z.record(z.string(), z.array(z.string())).optional(),
});

export type ProblemJson = z.infer<typeof problemJsonSchema>;

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = new.target.name;
  }
}

/** No credentials, or the token was rejected. The client has already asked for a renewal. */
export class ApiUnauthorized extends ApiError {
  constructor() {
    super('Not authenticated', 401);
  }
}

/** Authenticated, but the role does not carry the policy the endpoint needs. */
export class ApiForbidden extends ApiError {
  constructor() {
    super('Not authorised', 403);
  }
}

/** The parent resource in a `GET /parent/{id}/children` call does not exist. */
export class ApiNotFound extends ApiError {
  constructor() {
    super('Not found', 404);
  }
}

/** A 400 with a field-keyed `errors` map. Keys are the API's PascalCase property names. */
export class ApiValidationProblem extends ApiError {
  constructor(
    readonly errors: Readonly<Record<string, readonly string[]>>,
    readonly detail: string | undefined,
  ) {
    super(detail ?? 'The request was not valid', 400);
  }
}

/** A domain-rule failure (usually 409). `detail` is safe to show the user verbatim. */
export class ApiDomainProblem extends ApiError {
  constructor(
    status: number,
    readonly title: string | undefined,
    readonly detail: string | undefined,
  ) {
    super(detail ?? title ?? 'The request could not be completed', status);
  }
}

/** The request never reached the server. */
export class ApiNetworkError extends ApiError {
  constructor(cause: unknown) {
    super('The server could not be reached', 0);
    this.cause = cause;
  }
}

// `Stops[0].Postcode` -> `stops.0.postcode`, the path shape react-hook-form's setError wants.
export function problemFieldToFormPath(field: string): string {
  return field
    .replace(/\[(\d+)\]/g, '.$1')
    .split('.')
    .map((segment) => (/^\d+$/.test(segment) ? segment : lowerFirst(segment)))
    .join('.');
}

function lowerFirst(value: string): string {
  const first = value.charAt(0);
  return first === '' ? value : first.toLowerCase() + value.slice(1);
}
