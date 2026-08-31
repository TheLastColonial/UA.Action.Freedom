import type { JSX } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { useReceivers } from '../../api/receivers';
import type { ReceiverReadModel } from '../../api/schemas/receivers';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { Pagination } from '../../components/Pagination';

const PAGE_SIZE = 50;

const columns: readonly Column<ReceiverReadModel>[] = [
  {
    header: 'Organisation',
    cell: (r) => <Link to={`/receivers/${encodeURIComponent(r.ref)}`}>{r.organisation}</Link>,
  },
  { header: 'Region', cell: (r) => r.region },
];

export function ReceiversListPage(): JSX.Element {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number(searchParams.get('page') ?? '1'));
  const query = useReceivers({ page, pageSize: PAGE_SIZE });

  const setPage = (next: number) => {
    setSearchParams((params) => {
      params.set('page', String(next));
      return params;
    });
  };

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>Receivers</h1>
        <Gate policy="receivers:write">
          <Link to="/receivers/new">New receiver</Link>
        </Gate>
      </header>
      <p>Delivery addresses and contacts are held separately and shown only to a Ground Officer.</p>

      {query.isPending ? <PageSkeleton /> : null}
      {query.isError ? <p role="alert">The receiver list could not be loaded.</p> : null}

      {query.isSuccess ? (
        <>
          <DataTable
            caption="Receivers"
            columns={columns}
            rows={query.data}
            rowKey={(r) => r.ref}
            emptyMessage="No receivers recorded yet."
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
