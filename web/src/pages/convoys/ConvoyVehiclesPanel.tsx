import type { JSX } from 'react';

import { useAssignVehicle, useConvoyVehicles, useUnassignVehicle } from '../../api/convoys';
import { ApiDomainProblem } from '../../api/problem';
import { Button, LinkButton } from '../../components/Button';
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
      header: 'Status',
      cell: (v) => (v.withdrawn ? (v.withdrawnReason ?? 'Withdrawn') : 'Travelling'),
    },
    {
      header: '',
      // A manifest is the paperwork for one vehicle on one convoy, so it is opened from the
      // truck-list entry rather than from a form that names a convoy and a VIN.
      cell: (v) =>
        v.withdrawn ? null : (
          <LinkButton
            to={`/convoys/${String(convoyId)}/vehicles/${encodeURIComponent(v.vin)}/manifest/new`}
          >
            Open manifest
          </LinkButton>
        ),
    },
    {
      header: '',
      // Before publication this takes the vehicle off the list. Afterwards it records that the
      // vehicle left — a breakdown — and the entry stays, because its manifest still describes a
      // real load.
      cell: (v) =>
        v.withdrawn ? null : (
          <Button
            type="button"
            variant="danger"
            disabled={unassign.isPending}
            onClick={() => {
              unassign.mutate(
                disabled ? { vin: v.vin, reason: 'Withdrawn by the dispatcher' } : { vin: v.vin },
              );
            }}
          >
            {disabled ? 'Withdraw' : 'Remove'}
          </Button>
        ),
    },
  ];

  return (
    <div>
      {disabled ? (
        <p role="status">
          The truck list is published — no more vehicles can join, but one that breaks down can be
          withdrawn.
        </p>
      ) : null}
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
