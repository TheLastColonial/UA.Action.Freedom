import type { JSX } from 'react';
import { useState } from 'react';

import {
  useAssignDriver,
  useConvoyVehicles,
  useUnassignDriver,
  useVehicleDrivers,
} from '../../api/convoys';
import { usePeople } from '../../api/people';
import { ApiDomainProblem } from '../../api/problem';
import type { ConvoyVehicleReadModel, VehicleDriverReadModel } from '../../api/schemas/convoys';
import type { PersonReadModel } from '../../api/schemas/people';
import { Button } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { SelectField } from '../../components/form/fields';

interface ConvoyDriversPanelProps {
  convoyId: number;
}

function problemMessage(error: unknown): string | undefined {
  return error instanceof ApiDomainProblem ? (error.detail ?? error.message) : undefined;
}

const fullName = (person: { firstName: string; lastName: string }) =>
  `${person.firstName} ${person.lastName}`;

function UndercrewedWarning({
  vehicles,
}: {
  vehicles: readonly ConvoyVehicleReadModel[];
}): JSX.Element | null {
  const undercrewed = vehicles.filter((vehicle) => vehicle.driverCount < 2);
  if (undercrewed.length === 0) {
    return null;
  }

  const one = undercrewed.length === 1;
  const names = undercrewed.map((vehicle) => `${vehicle.vin} (${vehicle.plate})`).join(', ');
  return (
    <p role="status">
      {one ? 'Vehicle' : 'Vehicles'} {names} {one ? 'has' : 'have'} fewer than two drivers assigned.
      This is advisory only — nothing is blocked.
    </p>
  );
}

export function ConvoyDriversPanel({ convoyId }: ConvoyDriversPanelProps): JSX.Element {
  const vehiclesQuery = useConvoyVehicles(convoyId);
  const driversQuery = usePeople({ page: 1, pageSize: 200, driversOnly: true });

  if (vehiclesQuery.isPending || driversQuery.isPending) {
    return <PageSkeleton />;
  }
  if (vehiclesQuery.isError) {
    return <p role="alert">The vehicles could not be loaded.</p>;
  }
  if (driversQuery.isError) {
    return <p role="alert">The driver list could not be loaded.</p>;
  }

  const vehicles = 'parentMissing' in vehiclesQuery.data ? [] : vehiclesQuery.data;
  if (vehicles.length === 0) {
    return <p>Put vehicles on the truck list before assigning drivers.</p>;
  }

  return (
    <div>
      <UndercrewedWarning vehicles={vehicles} />
      {vehicles.map((vehicle) => (
        <section key={vehicle.vin} aria-label={`Crew for ${vehicle.plate}`}>
          <h3>{vehicle.plate}</h3>
          <VehicleCrew convoyId={convoyId} vehicle={vehicle} drivers={driversQuery.data} />
        </section>
      ))}
    </div>
  );
}

interface VehicleCrewProps {
  convoyId: number;
  vehicle: ConvoyVehicleReadModel;
  drivers: readonly PersonReadModel[];
}

function VehicleCrew({ convoyId, vehicle, drivers }: VehicleCrewProps): JSX.Element {
  const crewQuery = useVehicleDrivers(convoyId, vehicle.vin);
  const [selected, setSelected] = useState('');
  const assign = useAssignDriver(convoyId, vehicle.vin);
  const unassign = useUnassignDriver(convoyId, vehicle.vin);

  if (crewQuery.isPending) {
    return <p>Loading drivers…</p>;
  }
  if (crewQuery.isError) {
    return <p role="alert">The crew for {vehicle.plate} could not be loaded.</p>;
  }

  const crew = 'parentMissing' in crewQuery.data ? [] : crewQuery.data;
  const crewIds = new Set(crew.map((member) => member.personId));
  const options = [
    { value: '', label: 'Select a driver' },
    ...drivers
      .filter((driver) => !crewIds.has(driver.id))
      .map((driver) => ({ value: driver.id, label: fullName(driver) })),
  ];
  const error = problemMessage(assign.error) ?? problemMessage(unassign.error);

  return (
    <div>
      <DataTable<VehicleDriverReadModel>
        caption={`Drivers for ${vehicle.plate}`}
        columns={[
          { header: 'Name', cell: fullName },
          {
            header: 'Action',
            cell: (member) => (
              <Gate policy="convoys:assign-drivers">
                <Button
                  variant="secondary"
                  aria-label={`Remove ${fullName(member)}`}
                  disabled={unassign.isPending}
                  onClick={() => {
                    unassign.mutate(member.personId);
                  }}
                >
                  Remove
                </Button>
              </Gate>
            ),
          },
        ]}
        rows={crew}
        rowKey={(member) => member.personId}
        emptyMessage="No drivers assigned yet"
      />

      <Gate policy="convoys:assign-drivers">
        <form
          style={{ marginTop: 'var(--space-3)', display: 'flex', gap: 'var(--space-2)' }}
          onSubmit={(event) => {
            event.preventDefault();
            if (selected) {
              assign.mutate(selected, {
                onSuccess: () => {
                  setSelected('');
                },
              });
            }
          }}
        >
          <SelectField
            label={`Add driver to ${vehicle.plate}`}
            value={selected}
            onChange={(event) => {
              setSelected(event.target.value);
            }}
            options={options}
            disabled={assign.isPending}
          />
          <Button type="submit" disabled={!selected || assign.isPending}>
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
