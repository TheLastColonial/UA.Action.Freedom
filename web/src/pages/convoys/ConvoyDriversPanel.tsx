import type { JSX } from 'react';
import { useState } from 'react';

import {
  useAssignCrew,
  useConvoyVehicles,
  useUnassignCrew,
  useVehicleCrew,
} from '../../api/convoys';
import { usePeople } from '../../api/people';
import { ApiDomainProblem } from '../../api/problem';
import { journeyLegLabels, journeyLegSchema } from '../../api/schemas/common';
import type { JourneyLeg } from '../../api/schemas/common';
import { crewRoleSchema } from '../../api/schemas/convoys';
import type {
  ConvoyVehicleReadModel,
  CrewRole,
  VehicleCrewReadModel,
} from '../../api/schemas/convoys';
import type { PersonReadModel } from '../../api/schemas/people';
import { Button } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { SelectField } from '../../components/form/fields';
import { VehicleInsurancePanel } from './VehicleInsurancePanel';

interface ConvoyDriversPanelProps {
  convoyId: number;
}

function problemMessage(error: unknown): string | undefined {
  return error instanceof ApiDomainProblem ? (error.detail ?? error.message) : undefined;
}

const fullName = (person: { firstName: string; lastName: string }) =>
  `${person.firstName} ${person.lastName}`;

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
      {vehicles.map((vehicle) => (
        <section key={vehicle.vin} aria-label={`Crew for ${vehicle.plate}`}>
          <h3>{vehicle.plate}</h3>
          {vehicle.withdrawn ? (
            <p>
              Withdrawn from this convoy
              {vehicle.withdrawnReason ? `: ${vehicle.withdrawnReason}` : null}. Its crew is the
              record of who set off and cannot change.
            </p>
          ) : (
            <VehicleCrew convoyId={convoyId} vehicle={vehicle} volunteers={peopleQuery.data} />
          )}
          <VehicleInsurancePanel convoyId={convoyId} vin={vehicle.vin} plate={vehicle.plate} />
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

// A vehicle is crewed twice, with a handover at the European border, so the leg is part of every
// assignment rather than something the form can leave out.
const LEG_OPTIONS = journeyLegSchema.options.map((leg) => ({
  value: leg,
  label: journeyLegLabels[leg],
}));

function VehicleCrew({ convoyId, vehicle, volunteers }: VehicleCrewProps): JSX.Element {
  const crewQuery = useVehicleCrew(convoyId, vehicle.vin);
  const [leg, setLeg] = useState<JourneyLeg>('Uk');
  const [role, setRole] = useState<CrewRole>('Driver');
  const [selected, setSelected] = useState('');
  const assign = useAssignCrew(convoyId, vehicle.vin);
  const unassign = useUnassignCrew(convoyId, vehicle.vin);

  if (crewQuery.isPending) {
    return <p>Loading drivers…</p>;
  }
  if (crewQuery.isError) {
    return <p role="alert">The crew for {vehicle.plate} could not be loaded.</p>;
  }

  const crew = 'parentMissing' in crewQuery.data ? [] : crewQuery.data;
  // One seat per person per leg: somebody crewing the UK leg is still free for the border one.
  const seatedOnThisLeg = new Set(
    crew.filter((member) => member.leg === leg).map((member) => member.personId),
  );
  // A driver must be registered to drive; a passenger can be any volunteer.
  const eligible = volunteers.filter(
    (person) => !seatedOnThisLeg.has(person.id) && (role === 'Passenger' || person.isDriver),
  );
  const noun = role === 'Driver' ? 'driver' : 'passenger';
  const options = [
    { value: '', label: `Select a ${noun}` },
    ...eligible.map((person) => ({ value: person.id, label: fullName(person) })),
  ];
  const error = problemMessage(assign.error) ?? problemMessage(unassign.error);

  return (
    <div>
      <DataTable<VehicleCrewReadModel>
        caption={`Crew for ${vehicle.plate}`}
        columns={[
          { header: 'Name', cell: fullName },
          { header: 'Leg', cell: (member) => journeyLegLabels[member.leg] },
          { header: 'Role', cell: (member) => member.role },
          {
            header: 'Action',
            cell: (member) => (
              <Gate policy="convoys:assign-drivers">
                <Button
                  variant="secondary"
                  aria-label={`Remove ${fullName(member)} from the ${journeyLegLabels[member.leg]} leg`}
                  disabled={unassign.isPending}
                  onClick={() => {
                    unassign.mutate({ personId: member.personId, leg: member.leg });
                  }}
                >
                  Remove
                </Button>
              </Gate>
            ),
          },
        ]}
        rows={crew}
        rowKey={(member) => `${member.personId}:${member.leg}`}
        emptyMessage="No crew assigned yet"
      />

      <Gate policy="convoys:assign-drivers">
        <form
          style={{ marginTop: 'var(--space-3)', display: 'flex', gap: 'var(--space-2)' }}
          onSubmit={(event) => {
            event.preventDefault();
            if (selected) {
              assign.mutate(
                { personId: selected, leg, role },
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
            label={`Leg for ${vehicle.plate}`}
            value={leg}
            onChange={(event) => {
              setLeg(journeyLegSchema.parse(event.target.value));
              setSelected('');
            }}
            options={LEG_OPTIONS}
            disabled={assign.isPending}
          />
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
