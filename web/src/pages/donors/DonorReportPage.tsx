import type { JSX } from 'react';
import { useParams } from 'react-router-dom';

import { useDonorReport } from '../../api/donations';
import type { DonorItemStatus } from '../../api/schemas/donations';
import { ApiNotFound } from '../../api/problem';
import { Button } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import { DetailCard } from '../../components/DetailCard';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import './DonorReportPage.css';

const STATUS_TEXT: Record<DonorItemStatus, string> = {
  BeingPacked: 'Being packed',
  PackedAndChecked: 'Packed and checked',
};

const poundsFormat = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' });

// A report a user prints or saves for the donor, who has no login (O6). It is high level on purpose: what was
// given, what it was worth and how far it has got. The API sends nothing about where the goods are going.
export function DonorReportPage(): JSX.Element {
  const { id = '' } = useParams();
  const query = useDonorReport(id);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The donor report could not be loaded.</p>;
  }

  const report = query.data;

  return (
    <section className="donor-report">
      <header className="donor-report__header">
        <h1>Donation report for {report.donorName}</h1>
        <span className="donor-report__print">
          <Button
            variant="secondary"
            onClick={() => {
              window.print();
            }}
          >
            Print
          </Button>
        </span>
      </header>

      <p>
        {String(report.itemCount)} {report.itemCount === 1 ? 'item' : 'items'} given, worth{' '}
        {poundsFormat.format(report.totalValueGbp)} in all.
      </p>

      <DetailCard title="By kind of goods">
        <DataTable
          caption="Items and value by category"
          columns={[
            { header: 'Category', cell: (line) => line.categoryNameEn },
            { header: 'Items', cell: (line) => String(line.items) },
            { header: 'Value', cell: (line) => poundsFormat.format(line.valueGbp) },
          ]}
          rows={report.byCategory}
          rowKey={(line) => line.categoryNameEn}
          emptyMessage="Nothing from this donor has been packed yet."
        />
      </DetailCard>

      {report.donations.map((donation) => (
        <DetailCard
          key={donation.donationId}
          title={`Received ${donation.receivedOn.slice(0, 10)}`}
        >
          <DataTable
            caption={`Donation ${String(donation.donationId)}`}
            columns={[
              { header: 'Category', cell: (item) => item.categoryNameEn },
              { header: 'Quantity', cell: (item) => String(item.quantity) },
              {
                header: 'Value',
                cell: (item) => (item.valueGbp === null ? '—' : poundsFormat.format(item.valueGbp)),
              },
              { header: 'Status', cell: (item) => STATUS_TEXT[item.status] },
            ]}
            rows={donation.items.map((item, index) => ({ ...item, key: index }))}
            rowKey={(item) => String(item.key)}
            emptyMessage="No items."
          />
        </DetailCard>
      ))}
    </section>
  );
}
