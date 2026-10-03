import { useReceivers } from '../../api/receivers';
import type { ReceiverReadModel } from '../../api/schemas/receivers';
import { RECEIVER_STATUS_LABELS, canReceive } from './receiverStatus';

export interface ReceiverOption {
  value: string;
  label: string;
}

// The receivers a person may pick as a destination: registered ones only, showing organisation and
// region and nothing else. The one already chosen is kept even when it no longer qualifies, labelled
// with why, so an edit never silently drops it.
export function receiverOptions(
  receivers: readonly ReceiverReadModel[],
  current: string,
): ReceiverOption[] {
  return receivers
    .filter((receiver) => canReceive(receiver.status) || receiver.ref === current)
    .map((receiver) => ({
      value: receiver.ref,
      label: canReceive(receiver.status)
        ? `${receiver.organisation} — ${receiver.region}`
        : `${receiver.organisation} — ${receiver.region} (${RECEIVER_STATUS_LABELS[receiver.status]})`,
    }));
}

export function useReceiverOptions(current: string): {
  options: ReceiverOption[];
  isPending: boolean;
} {
  const receivers = useReceivers({ page: 1, pageSize: 200 });
  return {
    options: receiverOptions(receivers.data ?? [], current),
    isPending: receivers.isPending,
  };
}
