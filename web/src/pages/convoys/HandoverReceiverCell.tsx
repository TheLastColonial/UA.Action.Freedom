import type { JSX } from 'react';

import { useSetHandoverReceiver } from '../../api/convoys';
import type { ConvoyVehicleReadModel } from '../../api/schemas/convoys';
import { Gate } from '../../components/Gate';
import { useReceiverOptions } from '../receivers/useRegisteredReceivers';

interface HandoverReceiverCellProps {
  convoyId: number;
  vehicle: ConvoyVehicleReadModel;
  onError: (message: string | undefined) => void;
}

// The Receiver a vehicle is handed over to in Ukraine. Only registered receivers are offered; the API
// refuses any other. A dispatcher sets it, everyone who can see the convoy can read it.
export function HandoverReceiverCell({
  convoyId,
  vehicle,
  onError,
}: HandoverReceiverCellProps): JSX.Element | null {
  const current = vehicle.handoverReceiverRef ?? '';
  const { options } = useReceiverOptions(current);
  const set = useSetHandoverReceiver(convoyId);

  const name = options.find((option) => option.value === current)?.label ?? '—';

  if (vehicle.withdrawn) {
    return <span>{name}</span>;
  }

  return (
    <Gate policy="convoys:write" fallback={<span>{name}</span>}>
      <select
        aria-label={`Handover receiver for ${vehicle.vin}`}
        value={current}
        disabled={set.isPending}
        onChange={(event) => {
          onError(undefined);
          if (event.target.value === '') {
            return;
          }
          set.mutate(
            { vin: vehicle.vin, receiverRef: event.target.value },
            {
              onError: (error) => {
                onError(error.message);
              },
            },
          );
        }}
      >
        <option value="">Not yet chosen</option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </Gate>
  );
}
