import type { ReceiverStatus } from '../../api/schemas/receivers';

// A receiver may be sent to only while it is registered. The label says that and nothing more: the
// status never records what kind of body a receiver is (decision D33).
export const RECEIVER_STATUS_LABELS: Record<ReceiverStatus, string> = {
  Pending: 'Pending registration',
  Registered: 'Registered',
  Suspended: 'Suspended',
  Expired: 'Registration expired',
};

export const RECEIVER_STATUSES: readonly ReceiverStatus[] = [
  'Pending',
  'Registered',
  'Suspended',
  'Expired',
];

export function canReceive(status: ReceiverStatus): boolean {
  return status === 'Registered';
}
