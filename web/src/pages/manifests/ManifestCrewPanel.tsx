import type { JSX } from 'react';

import { useManifestCrew } from '../../api/manifests';
import { journeyLegLabels, journeyLegSchema } from '../../api/schemas/common';
import type { VehicleCrewReadModel } from '../../api/schemas/convoys';
import { DataTable } from '../../components/DataTable';
import { Spinner } from '../../components/Spinner';

interface ManifestCrewPanelProps {
  manifestId: string;
  convoyId: number;
  vin: string;
}

const fullName = (member: { firstName: string; lastName: string }) =>
  `${member.firstName} ${member.lastName}`;

/**
 * Who is travelling with this manifest's vehicle, per leg.
 *
 * Read-only on purpose. Crewing happens once, on the convoy's truck-list entry, and this reports
 * it — the manifest used to keep its own driver teams, written here, connected to the convoy's
 * crew by nothing at all. A printed manifest could then name people who were not in the vehicle
 * while the insurance that actually gates departure covered somebody else.
 */
export function ManifestCrewPanel({
  manifestId,
  convoyId,
  vin,
}: ManifestCrewPanelProps): JSX.Element {
  const query = useManifestCrew(manifestId);

  if (query.isPending) {
    return <Spinner label="Loading crew…" />;
  }
  if (query.isError) {
    return <p role="alert">The crew could not be loaded.</p>;
  }

  const crew = 'parentMissing' in query.data ? [] : query.data;

  return (
    <section aria-label="Crew">
      <h2>Crew</h2>
      <p>
        Crewed on the convoy, not here —{' '}
        <a href={`/convoys/${String(convoyId)}`}>manage the crew of {vin} on its convoy</a>.
      </p>

      {journeyLegSchema.options.map((leg) => (
        <DataTable<VehicleCrewReadModel>
          key={leg}
          caption={`${journeyLegLabels[leg]} leg`}
          columns={[
            { header: 'Name', cell: fullName },
            { header: 'Role', cell: (member) => member.role },
          ]}
          rows={crew.filter((member) => member.leg === leg)}
          rowKey={(member) => member.personId}
          emptyMessage="Nobody crewing this leg yet"
        />
      ))}
    </section>
  );
}
