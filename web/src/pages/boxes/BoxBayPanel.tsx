import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { useBays } from '../../api/locations';
import { usePeople } from '../../api/people';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { useAssignBoxBay, useBoxBay, useBoxBayHistory, useVacateBoxBay } from '../../api/boxes';
import { Button } from '../../components/Button';
import { Gate } from '../../components/Gate';
import { SelectField } from '../../components/form/fields';
import { assignBayFormSchema, assignBayFormToRequest, emptyAssignBayForm } from './boxModels';
import type { AssignBayFormValues } from './boxModels';

interface BoxBayPanelProps {
  boxId: number;
  locationId: number | null;
}

export function BoxBayPanel({ boxId, locationId }: BoxBayPanelProps): JSX.Element {
  const current = useBoxBay(boxId);
  const history = useBoxBayHistory(boxId);
  const bays = useBays(locationId ?? -1, { enabled: locationId !== null });
  const volunteers = usePeople({ page: 1, pageSize: 200 });
  const assign = useAssignBoxBay(boxId);
  const vacate = useVacateBoxBay(boxId);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<AssignBayFormValues>({
    resolver: zodResolver(assignBayFormSchema),
    defaultValues: emptyAssignBayForm(),
  });

  const availableBays = bays.data && !('parentMissing' in bays.data) ? bays.data : [];
  const bayCode = (bayId: number) =>
    availableBays.find((bay) => bay.id === bayId)?.code ?? String(bayId);

  const message =
    assign.error instanceof ApiNotFound
      ? 'That bay or volunteer could not be found.'
      : assign.error instanceof ApiDomainProblem
        ? (assign.error.detail ?? assign.error.message)
        : undefined;

  const bayOptions = [
    { value: '', label: 'Select a bay…' },
    ...availableBays.map((bay) => ({ value: String(bay.id), label: bay.code })),
  ];
  const volunteerOptions = [
    { value: '', label: 'Select a volunteer…' },
    ...(volunteers.data ?? []).map((person) => ({
      value: person.id,
      label: `${person.firstName} ${person.lastName}`,
    })),
  ];

  return (
    <div>
      <h2>Bay</h2>

      {current.isPending ? <p>Loading…</p> : null}
      {current.isSuccess ? (
        current.data ? (
          <p>
            Currently in bay <strong>{bayCode(current.data.bayId)}</strong>.
          </p>
        ) : (
          <p>Not currently in a bay.</p>
        )
      ) : null}

      {history.isSuccess && history.data.length > 0 ? (
        <details>
          <summary>Bay history</summary>
          <ul>
            {history.data.map((entry) => (
              <li key={entry.id}>
                {bayCode(entry.bayId)} —{' '}
                {entry.active ? 'current' : `vacated ${entry.vacatedAt ?? ''}`}
              </li>
            ))}
          </ul>
        </details>
      ) : null}

      <Gate policy="boxes:allocate-bay">
        {locationId === null ? (
          <p>This box has no location yet — set one before placing it in a bay.</p>
        ) : (
          <>
            <form
              noValidate
              onSubmit={(event) => {
                void handleSubmit((values) => {
                  assign.mutate(assignBayFormToRequest(values));
                })(event);
              }}
            >
              {message ? (
                <p role="alert" className="field__error">
                  {message}
                </p>
              ) : null}

              <SelectField
                label="Bay"
                options={bayOptions}
                error={errors.bayId?.message}
                {...register('bayId')}
              />
              <SelectField
                label="Placed by"
                options={volunteerOptions}
                error={errors.assignedByPersonId?.message}
                {...register('assignedByPersonId')}
              />

              <Button type="submit" disabled={assign.isPending}>
                Place in bay
              </Button>
            </form>

            {current.data ? (
              <Button
                type="button"
                variant="secondary"
                disabled={vacate.isPending}
                onClick={() => {
                  vacate.mutate();
                }}
              >
                Vacate bay
              </Button>
            ) : null}
          </>
        )}
      </Gate>
    </div>
  );
}
