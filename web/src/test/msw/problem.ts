import { HttpResponse } from 'msw';

export function problem(status: number, detail: string, extra: Record<string, unknown> = {}) {
  return HttpResponse.json(
    { title: 'Request failed', status, detail, ...extra },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );
}

export function validationProblem(errors: Record<string, string[]>) {
  return HttpResponse.json(
    { title: 'Validation failed', status: 400, errors },
    { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
  );
}
