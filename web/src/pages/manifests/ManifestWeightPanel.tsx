import type { JSX } from 'react';

import { useManifestWeight } from '../../api/manifests';
import { PageSkeleton } from '../../components/PageSkeleton';

export function ManifestWeightPanel({ manifestId }: { manifestId: string }): JSX.Element {
  const query = useManifestWeight(manifestId);

  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The manifest weight could not be loaded.</p>;
  }

  const weight = query.data;

  return (
    <div>
      <h2>Border-check weight</h2>
      {weight.unvalidatedBoxCount > 0 ? (
        <p role="status">
          {weight.unvalidatedBoxCount} box(es) are not yet validated — this total is provisional.
        </p>
      ) : null}
      <dl>
        <dt>Vehicle</dt>
        <dd>{weight.vehicleKg} kg</dd>
        <dt>Cargo</dt>
        <dd>{weight.cargoKg} kg</dd>
        <dt>Crew and bags</dt>
        <dd>{weight.crewAndBagsKg} kg</dd>
        <dt>Fuel</dt>
        <dd>{weight.fuelKg} kg</dd>
        <dt>Total</dt>
        <dd>
          <strong>{weight.totalKg} kg</strong>
        </dd>
      </dl>
    </div>
  );
}
