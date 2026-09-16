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
      {weight.cargoOverweight ? (
        <p role="status">
          Cargo is over the vehicle's stated capacity of {weight.maxCargoWeightKg} kg. This is
          advisory only — nothing is blocked.
        </p>
      ) : null}
      {weight.oversizedBoxIds.length > 0 ? (
        <p role="status">
          Box(es) {weight.oversizedBoxIds.map((id) => `#${String(id)}`).join(', ')} may not fit the
          vehicle's cargo space. This is advisory only — nothing is blocked.
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
        {weight.maxCargoWeightKg !== null ? (
          <>
            <dt>Vehicle's maximum cargo weight</dt>
            <dd>{weight.maxCargoWeightKg} kg</dd>
          </>
        ) : null}
      </dl>
    </div>
  );
}
