import type { JSX } from 'react';
import { Link } from 'react-router-dom';

import { useManifestBoxes } from '../../api/manifests';
import type { ManifestBoxReadModel } from '../../api/schemas/manifests';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { PageSkeleton } from '../../components/PageSkeleton';

interface ManifestBoxesPanelProps {
  manifestId: string;
  convoyId: number;
  vin: string;
}

const columns: readonly Column<ManifestBoxReadModel>[] = [
  { header: 'Box', cell: (b) => `#${String(b.boxId)}` },
  { header: 'Weight (kg)', cell: (b) => b.weightKg },
  { header: 'Validated', cell: (b) => (b.validated ? 'Yes' : 'No') },
];

/**
 * The cargo the manifest's vehicle is carrying, read-only. Cargo is a box allocated to the
 * vehicle's truck-list entry (ADR 0004), so boxes are added and moved on the convoy, not here.
 */
export function ManifestBoxesPanel({
  manifestId,
  convoyId,
  vin,
}: ManifestBoxesPanelProps): JSX.Element {
  const query = useManifestBoxes(manifestId);

  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The manifest cargo could not be loaded.</p>;
  }

  const rows: readonly ManifestBoxReadModel[] = 'parentMissing' in query.data ? [] : query.data;

  return (
    <div>
      <h2>Cargo</h2>
      <p>
        Boxes are put on the vehicle from the convoy.{' '}
        <Link to={`/convoys/${String(convoyId)}?tab=cargo`}>
          Manage the cargo of {vin} on its convoy
        </Link>
      </p>

      <DataTable
        caption="Boxes on this manifest"
        columns={columns}
        rows={rows}
        rowKey={(b) => String(b.boxId)}
        emptyMessage="No boxes on this manifest yet."
      />
    </div>
  );
}
