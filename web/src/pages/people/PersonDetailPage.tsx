import type { JSX } from 'react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useDeletePerson, usePerson } from '../../api/people';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { Button, LinkButton } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';

export function PersonDetailPage(): JSX.Element {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const query = usePerson(id);
  const remove = useDeletePerson();
  const [confirming, setConfirming] = useState(false);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The volunteer could not be loaded.</p>;
  }

  const person = query.data;

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>
          {person.firstName} {person.lastName}
        </h1>
        <Gate policy="people:write">
          <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
            <LinkButton to={`/people/${encodeURIComponent(person.id)}/edit`} variant="secondary">
              Edit
            </LinkButton>
            <Button
              variant="danger"
              disabled={remove.isPending || confirming}
              onClick={() => {
                setConfirming(true);
              }}
            >
              Erase volunteer
            </Button>
          </span>
        </Gate>
      </header>

      {confirming ? (
        <div role="alertdialog" aria-labelledby="erase-heading" aria-describedby="erase-body">
          <h2 id="erase-heading">Erase this volunteer?</h2>
          <p id="erase-body">
            This permanently erases {person.firstName} {person.lastName}'s personal details. Past
            convoys and manifests they were part of will show them as a former volunteer. It cannot
            be undone.
          </p>
          <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
            <Button
              variant="danger"
              disabled={remove.isPending}
              onClick={() => {
                remove.mutate(person.id, {
                  onSuccess: () => {
                    void navigate('/people');
                  },
                  onError: () => {
                    setConfirming(false);
                  },
                });
              }}
            >
              Erase permanently
            </Button>
            <Button
              variant="secondary"
              onClick={() => {
                setConfirming(false);
              }}
            >
              Cancel
            </Button>
          </span>
        </div>
      ) : null}

      {remove.isError ? (
        <p role="alert">
          {remove.error instanceof ApiDomainProblem
            ? (remove.error.detail ?? remove.error.message)
            : 'The volunteer could not be erased.'}
        </p>
      ) : null}

      <DetailCard title="Personal details">
        <dl>
          <dt>Date of birth</dt>
          <dd>{person.dateOfBirth.slice(0, 10)}</dd>
          <dt>Phone</dt>
          <dd>{person.phone ?? '—'}</dd>
        </dl>
      </DetailCard>

      <DetailCard title="Volunteering">
        <dl>
          <dt>Joined</dt>
          <dd>{person.joined.slice(0, 10)}</dd>
          <dt>Volunteers to drive</dt>
          <dd>{person.isDriver ? 'Yes' : 'No'}</dd>
          <dt>Committed to a convoy</dt>
          <dd>{person.committed ? 'Yes' : 'No'}</dd>
        </dl>
      </DetailCard>
    </section>
  );
}
