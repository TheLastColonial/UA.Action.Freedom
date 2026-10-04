import type { JSX } from 'react';
import { useState } from 'react';
import { useQueries } from '@tanstack/react-query';

import {
  fetchVehicleCrew,
  useConvoyLeader,
  useConvoyVehicles,
  useNominateConvoyLeader,
} from '../../api/convoys';
import { ApiDomainProblem } from '../../api/problem';
import { qk } from '../../api/queryKeys';
import { Button } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { SelectField } from '../../components/form/fields';
import type { ConvoyLeaderAssignment } from '../../api/schemas/convoys';

interface ConvoyLeaderPanelProps {
  convoyId: number;
  arrived: boolean;
}

const when = (iso: string) => iso.slice(0, 16).replace('T', ' ');

// The leader is a Driver crewed on the convoy, so the choices are read from the crews of its vehicles.
export function ConvoyLeaderPanel({ convoyId, arrived }: ConvoyLeaderPanelProps): JSX.Element {
  const leaderQuery = useConvoyLeader(convoyId);
  const vehiclesQuery = useConvoyVehicles(convoyId);
  const nominate = useNominateConvoyLeader(convoyId);
  const [selected, setSelected] = useState('');

  const travelling = (
    vehiclesQuery.data && !('parentMissing' in vehiclesQuery.data) ? vehiclesQuery.data : []
  ).filter((vehicle) => !vehicle.withdrawn);
  const crews = useQueries({
    queries: travelling.map((vehicle) => ({
      queryKey: qk.convoys.vehicleCrew(convoyId, vehicle.vin),
      queryFn: () => fetchVehicleCrew(convoyId, vehicle.vin),
    })),
  });

  if (leaderQuery.isPending || vehiclesQuery.isPending) {
    return <PageSkeleton />;
  }
  if (leaderQuery.isError) {
    return <p role="alert">The convoy leader could not be loaded.</p>;
  }

  const drivers = crews
    .flatMap((crew) => (crew.data && !('parentMissing' in crew.data) ? crew.data : []))
    .filter((member) => member.role === 'Driver');
  const current = leaderQuery.data.current;
  const options = [
    { value: '', label: 'Select a leader' },
    ...drivers
      .filter((member) => member.personId !== current?.personId)
      .map((member) => ({
        value: member.personId,
        label: `${member.firstName} ${member.lastName}`,
      })),
  ];
  const error =
    nominate.error instanceof ApiDomainProblem
      ? (nominate.error.detail ?? nominate.error.message)
      : undefined;

  return (
    <section aria-label="Convoy leader">
      <h2>Convoy leader</h2>
      <p>{current ? `${current.personName} leads this convoy.` : 'No leader nominated yet.'}</p>

      {!arrived ? (
        <Gate policy="convoys:lead-assign">
          <SelectField
            label="Nominate leader"
            options={options}
            value={selected}
            onChange={(event) => {
              setSelected(event.target.value);
            }}
          />
          <Button
            type="button"
            disabled={selected === '' || nominate.isPending}
            onClick={() => {
              nominate.mutate(
                { personId: selected },
                {
                  onSuccess: () => {
                    setSelected('');
                  },
                },
              );
            }}
          >
            Nominate
          </Button>
        </Gate>
      ) : null}
      {error ? (
        <p role="alert" className="field__error">
          {error}
        </p>
      ) : null}

      <DataTable<ConvoyLeaderAssignment>
        caption="Leader history"
        columns={[
          { header: 'Leader', cell: (a) => a.personName },
          { header: 'From', cell: (a) => when(a.from) },
          { header: 'Until', cell: (a) => (a.until ? when(a.until) : 'Now') },
        ]}
        rows={leaderQuery.data.history}
        rowKey={(a) => String(a.id)}
        emptyMessage="Nobody has led this convoy yet."
      />
    </section>
  );
}
