import type { JSX } from 'react';
import { useState } from 'react';

import { useAssignLoader, useLoaders, useRemoveLoader } from '../../api/locations';
import { usePeople } from '../../api/people';
import { ApiDomainProblem } from '../../api/problem';
import { Button } from '../../components/Button';
import { PageSkeleton } from '../../components/PageSkeleton';
import { SelectField } from '../../components/form/fields';

interface LoadersPanelProps {
  locationId: number;
}

/**
 * Who manages this location (O14, O31, ADR 0010): a Loader sees only the locations with an open row here. Shown to
 * the Administrator alone, who is the only role the API lets read or write it.
 */
export function LoadersPanel({ locationId }: LoadersPanelProps): JSX.Element {
  const query = useLoaders(locationId);
  const people = usePeople({ page: 1, pageSize: 200 });
  const assign = useAssignLoader(locationId);
  const remove = useRemoveLoader(locationId);
  const [personId, setPersonId] = useState('');

  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The Loaders could not be loaded.</p>;
  }

  const open = query.data.filter((assignment) => assignment.until === null);
  const closed = query.data.filter((assignment) => assignment.until !== null);
  const assigned = new Set(open.map((assignment) => assignment.personId));
  const candidates = (people.data ?? []).filter((person) => !assigned.has(person.id));
  const message =
    assign.error instanceof ApiDomainProblem
      ? (assign.error.detail ?? assign.error.message)
      : undefined;

  return (
    <div>
      <h2>Loaders</h2>

      {open.length === 0 ? (
        <p>No Loader manages this location yet, so no Loader can see its boxes.</p>
      ) : (
        <ul>
          {open.map((assignment) => (
            <li
              key={assignment.id}
              style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-3)' }}
            >
              <span>{assignment.personName}</span>
              <Button
                type="button"
                variant="danger"
                disabled={remove.isPending}
                onClick={() => {
                  remove.mutate(assignment.personId);
                }}
              >
                Remove
              </Button>
            </li>
          ))}
        </ul>
      )}

      {closed.length > 0 ? (
        <p>Previously: {closed.map((assignment) => assignment.personName).join(', ')}</p>
      ) : null}

      <form
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          if (personId === '') {
            return;
          }
          assign.mutate(personId, {
            onSuccess: () => {
              setPersonId('');
            },
          });
        }}
      >
        {message ? (
          <p role="alert" className="field__error">
            {message}
          </p>
        ) : null}
        <SelectField
          label="Volunteer"
          value={personId}
          onChange={(event) => {
            setPersonId(event.target.value);
          }}
          options={[
            { value: '', label: 'Choose a volunteer' },
            ...candidates.map((person) => ({
              value: person.id,
              label: `${person.firstName} ${person.lastName}`,
            })),
          ]}
        />
        <Button type="submit" disabled={assign.isPending || personId === ''}>
          Assign Loader
        </Button>
      </form>
    </div>
  );
}
