import type { JSX } from 'react';
import { useState } from 'react';

import { useAssignVehicle, useConvoyVehicles, useUnassignVehicle } from '../../api/convoys';
import { ApiDomainProblem } from '../../api/problem';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { PageSkeleton } from '../../components/PageSkeleton';
import type { ConvoyVehicleReadModel } from '../../api/schemas/convoys';

interface ConvoyVehiclesPanelProps {
  convoyId: number;
  disabled: boolean;
}

export function ConvoyVehiclesPanel({ convoyId, disabled }: ConvoyVehiclesPanelProps): JSX.Element {
  const query = useConvoyVehicles(convoyId);
  const assign = useAssignVehicle(convoyId);
  const unassign = useUnassignVehicle(convoyId);
  const [vin, setVin] = useState('');

  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The convoy vehicles could not be loaded.</p>;
  }

  const rows: readonly ConvoyVehicleReadModel[] = 'parentMissing' in query.data ? [] : query.data;

  const problemMessage = (error: unknown) =>
    error instanceof ApiDomainProblem ? (error.detail ?? error.message) : undefined;
  const message = problemMessage(assign.error) ?? problemMessage(unassign.error);

  const columns: readonly Column<ConvoyVehicleReadModel>[] = [
    { header: 'VIN', cell: (v) => v.vin },
    { header: 'Plate', cell: (v) => v.plate },
    { header: 'Weight (kg)', cell: (v) => v.weightKg },
    {
      header: '',
      cell: (v) => (
        <button
          type="button"
          disabled={disabled || unassign.isPending}
          onClick={() => {
            unassign.mutate(v.vin);
          }}
        >
          Remove
        </button>
      ),
    },
  ];

  return (
    <div>
      {disabled ? <p role="status">The truck list is published — vehicles are now fixed.</p> : null}
      {message ? (
        <p role="alert" className="field__error">
          {message}
        </p>
      ) : null}

      <DataTable
        caption="Vehicles on this convoy"
        columns={columns}
        rows={rows}
        rowKey={(v) => v.vin}
        emptyMessage="No vehicles assigned yet."
      />

      {!disabled ? (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            if (vin.trim().length > 0) {
              assign.mutate(vin.trim(), {
                onSuccess: () => {
                  setVin('');
                },
              });
            }
          }}
          style={{ display: 'flex', gap: 'var(--space-2)', alignItems: 'end' }}
        >
          <label style={{ display: 'flex', flexDirection: 'column' }}>
            VIN to assign
            <input
              value={vin}
              onChange={(event) => {
                setVin(event.target.value);
              }}
            />
          </label>
          <button type="submit" disabled={assign.isPending}>
            Assign vehicle
          </button>
        </form>
      ) : null}
    </div>
  );
}
