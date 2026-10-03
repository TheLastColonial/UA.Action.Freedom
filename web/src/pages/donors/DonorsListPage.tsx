import type { JSX } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { useDonors } from '../../api/donations';
import type { DonorReadModel } from '../../api/schemas/donations';
import { LinkButton } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { Pagination } from '../../components/Pagination';

const PAGE_SIZE = 50;

const columns: readonly Column<DonorReadModel>[] = [
  {
    header: 'Name',
    cell: (donor) => <Link to={`/donors/${encodeURIComponent(donor.id)}`}>{donor.name}</Link>,
  },
  { header: 'Email', cell: (donor) => donor.email ?? '—' },
  { header: 'Phone', cell: (donor) => donor.phone ?? '—' },
];

export function DonorsListPage(): JSX.Element {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number(searchParams.get('page') ?? '1'));

  const query = useDonors({ page, pageSize: PAGE_SIZE });

  const setPage = (next: number) => {
    setSearchParams((params) => {
      params.set('page', String(next));
      return params;
    });
  };

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>Donors</h1>
        <Gate policy="donations:write">
          <LinkButton to="/donors/new">New donor</LinkButton>
        </Gate>
      </header>

      {query.isPending ? <PageSkeleton /> : null}
      {query.isError ? <p role="alert">The donor list could not be loaded.</p> : null}

      {query.isSuccess ? (
        <>
          <DataTable
            caption="Donors"
            columns={columns}
            rows={query.data}
            rowKey={(donor) => donor.id}
            emptyMessage="No donors recorded yet."
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
