import type { JSX } from 'react';

import { useAssignVehicle, useConvoyVehicles, useUnassignVehicle } from '../../api/convoys';
import { ApiDomainProblem } from '../../api/problem';
import { Button } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { PageSkeleton } from '../../components/PageSkeleton';
import type { ConvoyVehicleReadModel } from '../../api/schemas/convoys';
import { VehicleSearchDropdown } from '../../components/form/VehicleSearchDropdown';

interface ConvoyVehiclesPanelProps {
  convoyId: number;
  disabled: boolean;
}

export function ConvoyVehiclesPanel({ convoyId, disabled }: ConvoyVehiclesPanelProps): JSX.Element {
  const query = useConvoyVehicles(convoyId);
  const assign = useAssignVehicle(convoyId);
  const unassign = useUnassignVehicle(convoyId);

  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The convoy vehicles could not be loaded.</p>;
  }

  const rows = 'parentMissing' in query.data ? [] : query.data;
  const assignedVins = rows.map((v) => v.vin);

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
        <Button
          type="button"
          variant="danger"
          disabled={disabled || unassign.isPending}
          onClick={() => {
            unassign.mutate(v.vin);
          }}
        >
          Remove
        </Button>
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
        <div>
          <VehicleSearchDropdown
            onSelect={(vin) => {
              assign.mutate(vin);
            }}
            disabled={assign.isPending}
            excludeVins={assignedVins}
          />
        </div>
      ) : null}
    </div>
  );
}
