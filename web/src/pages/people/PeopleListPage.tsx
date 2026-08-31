import type { JSX } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { usePeople } from '../../api/people';
import type { PersonReadModel } from '../../api/schemas/people';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { Pagination } from '../../components/Pagination';

const PAGE_SIZE = 50;

const columns: readonly Column<PersonReadModel>[] = [
  {
    header: 'Name',
    cell: (p) => (
      <Link to={`/people/${encodeURIComponent(p.id)}`}>
        {p.firstName} {p.lastName}
      </Link>
    ),
  },
  { header: 'Driver', cell: (p) => (p.isDriver ? 'Yes' : 'No') },
  { header: 'Committed', cell: (p) => (p.committed ? 'Yes' : 'No') },
  { header: 'Joined', cell: (p) => p.joined.slice(0, 10) },
];

export function PeopleListPage(): JSX.Element {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number(searchParams.get('page') ?? '1'));
  const driversOnly = searchParams.get('driversOnly') === 'true';

  const query = usePeople({ page, pageSize: PAGE_SIZE, driversOnly });

  const setPage = (next: number) => {
    setSearchParams((params) => {
      params.set('page', String(next));
      return params;
    });
  };

  const toggleDriversOnly = (next: boolean) => {
    setSearchParams((params) => {
      if (next) {
        params.set('driversOnly', 'true');
      } else {
        params.delete('driversOnly');
      }
      params.delete('page');
      return params;
    });
  };

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>Volunteers</h1>
        <Gate policy="people:write">
          <Link to="/people/new">New volunteer</Link>
        </Gate>
      </header>

      <label
        style={{ display: 'inline-flex', gap: 'var(--space-2)', marginBlockEnd: 'var(--space-4)' }}
      >
        <input
          type="checkbox"
          checked={driversOnly}
          onChange={(event) => {
            toggleDriversOnly(event.target.checked);
          }}
        />
        Drivers only
      </label>

      {query.isPending ? <PageSkeleton /> : null}
      {query.isError ? <p role="alert">The volunteer list could not be loaded.</p> : null}

      {query.isSuccess ? (
        <>
          <DataTable
            caption="Volunteers"
            columns={columns}
            rows={query.data}
            rowKey={(p) => p.id}
            emptyMessage="No volunteers recorded yet."
          />
          <Pagination
            page={page}
            hasNext={query.data.length === PAGE_SIZE}
            onPageChange={setPage}
          />
        </>
      ) : null}
    </section>
  );
}
