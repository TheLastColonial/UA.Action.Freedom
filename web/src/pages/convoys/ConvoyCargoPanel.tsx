import type { JSX } from 'react';

import { useConvoyVehicles } from '../../api/convoys';
import { PageSkeleton } from '../../components/PageSkeleton';
import { VehicleCargoPanel } from './VehicleCargoPanel';
import { VehicleFerryPanel } from './VehicleFerryPanel';

interface ConvoyCargoPanelProps {
  convoyId: number;
}

/** What each vehicle on the truck list is carrying, and the ferry it is booked on. */
export function ConvoyCargoPanel({ convoyId }: ConvoyCargoPanelProps): JSX.Element {
  const query = useConvoyVehicles(convoyId);

  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The vehicles could not be loaded.</p>;
  }

  const vehicles = 'parentMissing' in query.data ? [] : query.data;
  if (vehicles.length === 0) {
    return <p>Put vehicles on the truck list before loading them.</p>;
  }

  return (
    <div>
      {vehicles.map((vehicle) => (
        <section key={vehicle.vin} aria-label={`Cargo and ferry for ${vehicle.plate}`}>
          <h3>{vehicle.plate}</h3>
          {vehicle.withdrawn ? (
            <p>
              Withdrawn from this convoy
              {vehicle.withdrawnReason ? `: ${vehicle.withdrawnReason}` : null}. It takes no more
              cargo; what it carried stays on its manifest.
            </p>
          ) : null}
          <VehicleCargoPanel
            convoyId={convoyId}
            vin={vehicle.vin}
            plate={vehicle.plate}
            withdrawn={vehicle.withdrawn}
          />
          <VehicleFerryPanel
            convoyId={convoyId}
            vin={vehicle.vin}
            plate={vehicle.plate}
            withdrawn={vehicle.withdrawn}
          />
        </section>
      ))}
    </div>
  );
}
