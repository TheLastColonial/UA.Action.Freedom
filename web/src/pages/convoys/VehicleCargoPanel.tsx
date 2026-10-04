import type { JSX } from 'react';
import { useState } from 'react';

import { useAllocateBox, useRemoveBoxAllocation, useVehicleBoxes } from '../../api/convoys';
import { ApiDomainProblem } from '../../api/problem';
import type { ManifestBoxReadModel } from '../../api/schemas/manifests';
import { Button } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { Spinner } from '../../components/Spinner';

interface VehicleCargoPanelProps {
  convoyId: number;
  vin: string;
  plate: string;
  withdrawn: boolean;
}

const problemMessage = (error: unknown) =>
  error instanceof ApiDomainProblem ? (error.detail ?? error.message) : undefined;

/**
 * A vehicle's cargo: the boxes allocated to its truck-list entry (ADR 0004). A box is on at most
 * one vehicle, so adding one that is already on another moves it.
 */
export function VehicleCargoPanel({
  convoyId,
  vin,
  plate,
  withdrawn,
}: VehicleCargoPanelProps): JSX.Element {
  const query = useVehicleBoxes(convoyId, vin);
  const allocate = useAllocateBox(convoyId, vin);
  const remove = useRemoveBoxAllocation(convoyId, vin);
  const [boxId, setBoxId] = useState('');

  if (query.isPending) {
    return <Spinner label="Loading cargo…" />;
  }
  if (query.isError) {
    return <p role="alert">The cargo for {plate} could not be loaded.</p>;
  }

  const rows: readonly ManifestBoxReadModel[] = 'parentMissing' in query.data ? [] : query.data;
  const message = problemMessage(allocate.error) ?? problemMessage(remove.error);

  const columns: readonly Column<ManifestBoxReadModel>[] = [
    { header: 'Box', cell: (b) => `#${String(b.boxId)}` },
    { header: 'Weight (kg)', cell: (b) => b.weightKg },
    { header: 'Validated', cell: (b) => (b.validated ? 'Yes' : 'No') },
    {
      header: '',
      cell: (b) => (
        <Gate policy="boxes:write">
          <Button
            type="button"
            variant="danger"
            disabled={remove.isPending}
            onClick={() => {
              remove.mutate(b.boxId);
            }}
          >
            Remove
          </Button>
        </Gate>
      ),
    },
  ];

  return (
    <div>
      {message ? (
        <p role="alert" className="field__error">
          {message}
        </p>
      ) : null}

      <DataTable
        caption={`Cargo on ${plate}`}
        columns={columns}
        rows={rows}
        rowKey={(b) => String(b.boxId)}
        emptyMessage="No boxes on this vehicle yet."
      />

      {withdrawn ? null : (
        <Gate policy="boxes:write">
          <form
            aria-label={`Add a box to ${plate}`}
            onSubmit={(event) => {
              event.preventDefault();
              const parsed = Number(boxId.trim());
              if (Number.isInteger(parsed) && parsed > 0) {
                allocate.mutate(parsed, {
                  onSuccess: () => {
                    setBoxId('');
                  },
                });
              }
            }}
            style={{ display: 'flex', gap: 'var(--space-2)', alignItems: 'end' }}
          >
            <label style={{ display: 'flex', flexDirection: 'column' }}>
              Box id to add to {plate}
              <input
                inputMode="numeric"
                value={boxId}
                onChange={(event) => {
                  setBoxId(event.target.value);
                }}
              />
            </label>
            <Button type="submit" disabled={allocate.isPending}>
              Add box
            </Button>
          </form>
        </Gate>
      )}
    </div>
  );
}
