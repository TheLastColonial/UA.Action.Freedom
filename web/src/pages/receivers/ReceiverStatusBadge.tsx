import type { JSX } from 'react';

import type { ReceiverStatus } from '../../api/schemas/receivers';
import { RECEIVER_STATUS_LABELS, canReceive } from './receiverStatus';

interface ReceiverStatusBadgeProps {
  status: ReceiverStatus;
}

export function ReceiverStatusBadge({ status }: ReceiverStatusBadgeProps): JSX.Element {
  return (
    <span
      data-status={status}
      style={{
        padding: '0 var(--space-2)',
        border: '1px solid var(--color-border)',
        borderRadius: 'var(--radius-md)',
        fontWeight: canReceive(status) ? 600 : 400,
      }}
    >
      {RECEIVER_STATUS_LABELS[status]}
    </span>
  );
}
