import { useQuery } from '@tanstack/react-query';
import type { UseQueryResult } from '@tanstack/react-query';
import { z } from 'zod';

import { getJson } from './http';
import { qk } from './queryKeys';
import { leftoverBookingTaskSchema } from './schemas/accommodation';
import type { LeftoverBookingTask } from './schemas/accommodation';
import { redeclareTaskSchema } from './schemas/declarations';
import type { RedeclareTask } from './schemas/declarations';

// The Dispatcher's task list for a convoy (D13, P16): re-declare tasks from stale declarations and leftover
// accommodation bookings, told apart by `type`. Both are derived on read, so resolving the thing clears the task.
const convoyTaskSchema = z.discriminatedUnion('type', [
  redeclareTaskSchema,
  leftoverBookingTaskSchema,
]);
type ConvoyTask = z.infer<typeof convoyTaskSchema>;

export const fetchConvoyTasks = (convoyId: number): Promise<readonly ConvoyTask[]> =>
  getJson(`/convoys/${String(convoyId)}/tasks`, z.array(convoyTaskSchema));

function useConvoyTasks<T>(
  convoyId: number,
  select: (tasks: readonly ConvoyTask[]) => readonly T[],
): UseQueryResult<readonly T[]> {
  return useQuery({
    queryKey: qk.declarations.tasks(convoyId),
    queryFn: () => fetchConvoyTasks(convoyId),
    select,
  });
}

const isRedeclare = (task: ConvoyTask): task is RedeclareTask => task.type === 'redeclare';
const isLeftover = (task: ConvoyTask): task is LeftoverBookingTask =>
  task.type === 'accommodation-leftover';

export const useRedeclareTasks = (convoyId: number): UseQueryResult<readonly RedeclareTask[]> =>
  useConvoyTasks(convoyId, (tasks) => tasks.filter(isRedeclare));

export const useLeftoverBookingTasks = (
  convoyId: number,
): UseQueryResult<readonly LeftoverBookingTask[]> =>
  useConvoyTasks(convoyId, (tasks) => tasks.filter(isLeftover));
