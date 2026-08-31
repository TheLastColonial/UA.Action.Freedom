import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { useManifestTeams, useSetManifestTeam } from '../../api/manifests';
import { usePeople } from '../../api/people';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import type { ManifestLeg } from '../../api/schemas/common';
import type { ManifestDriverTeamReadModel } from '../../api/schemas/manifests';
import { PageSkeleton } from '../../components/PageSkeleton';
import { SelectField } from '../../components/form/fields';
import { emptyTeamForm, teamFormSchema, teamFormToRequest } from './manifestModels';
import type { TeamFormValues } from './manifestModels';

interface ManifestTeamsPanelProps {
  manifestId: string;
  frozen: boolean;
}

const LEGS: readonly { leg: ManifestLeg; label: string }[] = [
  { leg: 'Uk', label: 'UK → Europe' },
  { leg: 'Border', label: 'Europe → Ukraine' },
];

export function ManifestTeamsPanel({ manifestId, frozen }: ManifestTeamsPanelProps): JSX.Element {
  const teams = useManifestTeams(manifestId);
  const drivers = usePeople({ page: 1, pageSize: 200, driversOnly: true });

  if (teams.isPending) {
    return <PageSkeleton />;
  }
  if (teams.isError) {
    return <p role="alert">The driver teams could not be loaded.</p>;
  }

  const current = 'parentMissing' in teams.data ? [] : teams.data;
  const driverOptions = [
    { value: '', label: 'Not assigned' },
    ...(drivers.data ?? []).map((person) => ({
      value: person.id,
      label: `${person.firstName} ${person.lastName}`,
    })),
  ];

  return (
    <div>
      <h2>Driver teams</h2>
      {frozen ? <p role="status">Frozen — driver teams can no longer be changed.</p> : null}
      {LEGS.map(({ leg, label }) => (
        <LegForm
          key={leg}
          manifestId={manifestId}
          leg={leg}
          label={label}
          frozen={frozen}
          current={current.find((team) => team.leg === leg)}
          driverOptions={driverOptions}
        />
      ))}
    </div>
  );
}

interface LegFormProps {
  manifestId: string;
  leg: ManifestLeg;
  label: string;
  frozen: boolean;
  current: ManifestDriverTeamReadModel | undefined;
  driverOptions: readonly { value: string; label: string }[];
}

function LegForm({
  manifestId,
  leg,
  label,
  frozen,
  current,
  driverOptions,
}: LegFormProps): JSX.Element {
  const setTeam = useSetManifestTeam(manifestId, leg);
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<TeamFormValues>({
    resolver: zodResolver(teamFormSchema),
    defaultValues: {
      ...emptyTeamForm(),
      primaryPersonId: current?.primaryPersonId ?? '',
      secondaryPersonId: current?.secondaryPersonId ?? '',
    },
  });

  const message =
    setTeam.error instanceof ApiNotFound
      ? 'One of the volunteers named for this leg is not on file.'
      : setTeam.error instanceof ApiDomainProblem
        ? (setTeam.error.detail ?? setTeam.error.message)
        : undefined;

  return (
    <form
      noValidate
      onSubmit={(event) => {
        void handleSubmit((values) => {
          setTeam.mutate(teamFormToRequest(values));
        })(event);
      }}
    >
      <fieldset disabled={frozen}>
        <legend>{label}</legend>
        {message ? (
          <p role="alert" className="field__error">
            {message}
          </p>
        ) : null}
        <SelectField
          label="Lead driver"
          options={driverOptions}
          error={errors.primaryPersonId?.message}
          {...register('primaryPersonId')}
        />
        <SelectField
          label="Second driver"
          options={driverOptions}
          error={errors.secondaryPersonId?.message}
          {...register('secondaryPersonId')}
        />
        <button type="submit" disabled={setTeam.isPending}>
          Save {label} team
        </button>
      </fieldset>
    </form>
  );
}
