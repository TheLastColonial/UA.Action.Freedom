import type { JSX } from 'react';

import { useConvoyReadiness } from '../../api/convoys';
import { DetailCard } from '../../components/DetailCard';

/**
 * Whether the convoy is ready to travel, and what is missing if not: two drivers and insurance
 * for each vehicle, and a route. Advisory — it blocks nothing.
 */
export function ConvoyReadinessPanel({ convoyId }: { convoyId: number }): JSX.Element {
  const query = useConvoyReadiness(convoyId);

  if (query.isPending) {
    return <p>Checking readiness…</p>;
  }
  if (query.isError) {
    return <p role="alert">Readiness could not be checked.</p>;
  }

  const readiness = query.data;
  const unready = readiness.vehicles.filter((vehicle) => !vehicle.ready);

  return (
    <DetailCard title="Readiness">
      <h3>{readiness.ready ? 'Ready to travel' : 'Not ready yet'}</h3>
      {readiness.reasons.length > 0 || unready.length > 0 ? (
        <ul>
          {readiness.reasons
            .filter((reason) => !reason.endsWith('not ready'))
            .map((reason) => (
              <li key={reason}>{reason}</li>
            ))}
          {unready.map((vehicle) => (
            <li key={vehicle.vin}>{`${vehicle.plate}: ${vehicle.reasons.join('; ')}`}</li>
          ))}
        </ul>
      ) : null}
      <p className="field__hint">This is advisory only — nothing is blocked by it.</p>
    </DetailCard>
  );
}
