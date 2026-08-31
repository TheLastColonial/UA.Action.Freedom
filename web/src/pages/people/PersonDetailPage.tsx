import type { JSX } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { useDeletePerson, usePerson } from '../../api/people';
import { ApiNotFound } from '../../api/problem';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';

export function PersonDetailPage(): JSX.Element {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const query = usePerson(id);
  const remove = useDeletePerson();

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
            <Link to={`/people/${encodeURIComponent(person.id)}/edit`}>Edit</Link>
            <button
              type="button"
              disabled={remove.isPending}
              onClick={() => {
                remove.mutate(person.id, {
                  onSuccess: () => {
                    void navigate('/people');
                  },
                });
              }}
            >
              Delete
            </button>
          </span>
        </Gate>
      </header>

      {remove.isError ? <p role="alert">The volunteer could not be removed.</p> : null}

      <dl>
        <dt>Date of birth</dt>
        <dd>{person.dateOfBirth.slice(0, 10)}</dd>
        <dt>Joined</dt>
        <dd>{person.joined.slice(0, 10)}</dd>
        <dt>Phone</dt>
        <dd>{person.phone ?? '—'}</dd>
        <dt>Volunteers to drive</dt>
        <dd>{person.isDriver ? 'Yes' : 'No'}</dd>
        <dt>Committed to a convoy</dt>
        <dd>{person.committed ? 'Yes' : 'No'}</dd>
      </dl>
    </section>
  );
}
