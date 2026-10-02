import { useQuery } from '@tanstack/react-query';
import type { UseQueryResult } from '@tanstack/react-query';

import { getJson } from './http';
import { qk } from './queryKeys';
import { meSchema } from './schemas/me';
import type { Me } from './schemas/me';

export function fetchMe(): Promise<Me> {
  return getJson('/me', meSchema);
}

/** Who the API says the caller is, and whether their login is linked to a volunteer. */
export function useMe(): UseQueryResult<Me> {
  return useQuery({ queryKey: qk.me, queryFn: fetchMe });
}
