import type { JSX } from 'react';
import { useState } from 'react';

import { useAssignDriver, useUnassignDriver, useVehicleDrivers, useConvoyVehicles } from '../../api/convoys';
import { usePeople } from '../../api/people';
import type { ParentMissing } from '../../api/client';
import { ApiDomainProblem } from '../../api/problem';
import { Button } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { SelectField } from '../../components/form/fields';

interface ConvoyDriversPanelProps {
  convoyId: number;
  disabled: boolean;
}

function problemMessage(error: unknown): string | undefined {
  return error instanceof ApiDomainProblem ? (error.detail ?? error.message) : undefined;
}

export function ConvoyDriversPanel({ convoyId, disabled }: ConvoyDriversPanelProps): JSX.Element {
  const vehiclesQuery = useConvoyVehicles(convoyId);
  const peopleQuery = usePeople({ page: 1, pageSize: 200, driversOnly: true });

  if (vehiclesQuery.isPending || peopleQuery.isPending) {
    return <PageSkeleton />;
  }

  if (vehiclesQuery.isError) {
    return <p role="alert">The vehicles could not be loaded.</p>;
  }

  if (peopleQuery.isError) {
    return <p role="alert">The driver list could not be loaded.</p>;
  }

  const vehicles = 'parentMissing' in vehiclesQuery.data ? [] : vehiclesQuery.data;
  const people = peopleQuery.data ?? [];

  const vehiclesWithFewerThanTwoDrivers = vehicles
    .filter((v) => v.driverCount < 2)
    .map((v) => `${v.vin} (${v.plate})`)
    .join(', ');

  return (
    <div>
      {vehiclesWithFewerThanTwoDrivers ? (
        <p role="status">
          Vehicle{vehicles.filter((v) => v.driverCount < 2).length === 1 ? '' : 's'}{' '}
          {vehiclesWithFewerThanTwoDrivers} {vehicles.filter((v) => v.driverCount < 2).length === 1 ? 'has' : 'have'} fewer
          than two drivers assigned. This is advisory only — nothing is blocked.
        </p>
      ) : null}

      {vehicles.map((vehicle) => (
        <div key={vehicle.vin} style={{ marginTop: '20px' }}>
          <h3>{vehicle.plate || vehicle.vin}</h3>
          <VehicleDriversSection
            convoyId={convoyId}
            vehicle={vehicle}
            people={people}
            disabled={disabled}
          />
        </div>
      ))}
    </div>
  );
}

interface VehicleDriversSectionProps {
  convoyId: number;
  vehicle: { vin: string; plate: string };
  people: Array<{ id: string; firstName: string; lastName: string }>;
  disabled: boolean;
}

function VehicleDriversSection({
  convoyId,
  vehicle,
  people,
  disabled,
}: VehicleDriversSectionProps): JSX.Element {
  const driversQuery = useVehicleDrivers(convoyId, vehicle.vin);
  const [selectedPersonId, setSelectedPersonId] = useState('');
  const assign = useAssignDriver(convoyId, vehicle.vin);
  const unassign = useUnassignDriver(convoyId, vehicle.vin);

  if (driversQuery.isPending) {
    return <p>Loading drivers...</p>;
  }

  const drivers = 'parentMissing' in driversQuery.data ? [] : driversQuery.data;
  const assignedIds = new Set(drivers.map((d) => d.personId));
  const availablePeople = people.filter((p) => !assignedIds.has(p.id));
  const driverOptions = [
    { value: '', label: 'Select a driver' },
    ...availablePeople.map((p) => ({ value: p.id, label: `${p.firstName} ${p.lastName}` })),
  ];

  const error = problemMessage(assign.error) ?? problemMessage(unassign.error);

  return (
    <div>
      <DataTable<{ personId: string; firstName: string; lastName: string }>
        caption={`Drivers for ${vehicle.plate || vehicle.vin}`}
        columns={[
          { header: 'Name', cell: (d) => `${d.firstName} ${d.lastName}` },
          {
            header: 'Action',
            cell: (d) => (
              <Gate policy="convoys:assign-drivers">
                <Button
                  variant="secondary"
                  disabled={unassign.isPending}
                  onClick={() => unassign.mutate(d.personId)}
                >
                  Remove
                </Button>
              </Gate>
            ),
          },
        ]}
        rows={drivers}
        rowKey="personId"
        emptyMessage="No drivers assigned yet"
      />

      <Gate policy="convoys:assign-drivers">
        <form
          style={{ marginTop: '16px', display: 'flex', gap: '8px' }}
          onSubmit={(e) => {
            e.preventDefault();
            if (selectedPersonId) {
              assign.mutate(selectedPersonId, {
                onSuccess: () => setSelectedPersonId(''),
              });
            }
          }}
        >
          <SelectField
            id={`driver-select-${vehicle.vin}`}
            label="Add driver"
            value={selectedPersonId}
            onChange={setSelectedPersonId}
            options={driverOptions}
            disabled={assign.isPending}
          />
          <Button type="submit" disabled={!selectedPersonId || assign.isPending}>
            Assign
          </Button>
        </form>
      </Gate>

      {error ? (
        <p role="alert" className="field__error">
          {error}
        </p>
      ) : null}
    </div>
  );
}
