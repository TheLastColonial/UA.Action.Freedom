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
import { crewRoleSchema } from '../../api/schemas/convoys';
import type {
  ConvoyVehicleReadModel,
  CrewRole,
  VehicleDriverReadModel,
} from '../../api/schemas/convoys';
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
  const peopleQuery = usePeople({ page: 1, pageSize: 200 });

  if (vehiclesQuery.isPending || peopleQuery.isPending) {
    return <PageSkeleton />;
  }
  if (vehiclesQuery.isError) {
    return <p role="alert">The vehicles could not be loaded.</p>;
  }
  if (peopleQuery.isError) {
    return <p role="alert">The volunteer list could not be loaded.</p>;
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
          <VehicleCrew convoyId={convoyId} vehicle={vehicle} volunteers={peopleQuery.data} />
        </section>
      ))}
    </div>
  );
}

interface VehicleCrewProps {
  convoyId: number;
  vehicle: ConvoyVehicleReadModel;
  volunteers: readonly PersonReadModel[];
}

const ROLE_OPTIONS = crewRoleSchema.options.map((role) => ({ value: role, label: role }));

function VehicleCrew({ convoyId, vehicle, volunteers }: VehicleCrewProps): JSX.Element {
  const crewQuery = useVehicleDrivers(convoyId, vehicle.vin);
  const [role, setRole] = useState<CrewRole>('Driver');
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
  // A driver must be registered to drive; a passenger can be any volunteer.
  const eligible = volunteers.filter(
    (person) => !crewIds.has(person.id) && (role === 'Passenger' || person.isDriver),
  );
  const noun = role === 'Driver' ? 'driver' : 'passenger';
  const options = [
    { value: '', label: `Select a ${noun}` },
    ...eligible.map((person) => ({ value: person.id, label: fullName(person) })),
  ];
  const error = problemMessage(assign.error) ?? problemMessage(unassign.error);

  return (
    <div>
      <DataTable<VehicleDriverReadModel>
        caption={`Crew for ${vehicle.plate}`}
        columns={[
          { header: 'Name', cell: fullName },
          { header: 'Role', cell: (member) => member.role },
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
        emptyMessage="No crew assigned yet"
      />

      <Gate policy="convoys:assign-drivers">
        <form
          style={{ marginTop: 'var(--space-3)', display: 'flex', gap: 'var(--space-2)' }}
          onSubmit={(event) => {
            event.preventDefault();
            if (selected) {
              assign.mutate(
                { personId: selected, role },
                {
                  onSuccess: () => {
                    setSelected('');
                  },
                },
              );
            }
          }}
        >
          <SelectField
            label={`Role on ${vehicle.plate}`}
            value={role}
            onChange={(event) => {
              setRole(crewRoleSchema.parse(event.target.value));
              setSelected('');
            }}
            options={ROLE_OPTIONS}
            disabled={assign.isPending}
          />
          <SelectField
            label={`Add ${noun} to ${vehicle.plate}`}
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
