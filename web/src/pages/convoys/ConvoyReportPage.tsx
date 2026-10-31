import type { JSX } from 'react';
import { useParams } from 'react-router-dom';

import { useConvoyReport } from '../../api/outcomes';
import { ApiNotFound } from '../../api/problem';
import { Button, LinkButton } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import type { ConvoyReport } from '../../api/schemas/outcomes';

type ReportVehicle = ConvoyReport['vehicles'][number];

const gbp = (value: number) =>
  new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' }).format(value);

const when = (value: string | null) => (value ? value.slice(0, 16).replace('T', ' ') : 'Not yet');

/**
 * The account of a convoy (O10): what was expected against what was spent, and which boxes were delivered or not. It
 * carries no address, contact or delivery detail — Receivers appear at organisation and region only — so it can be printed
 * and shared. It is drawn afresh every time it is opened, because closing does not lock the convoy (O25).
 */
export function ConvoyReportPage(): JSX.Element {
  const { id = '' } = useParams();
  const convoyId = Number(id);
  const query = useConvoyReport(convoyId);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The report could not be loaded.</p>;
  }

  const report = query.data;
  const budget = report.budget;

  const vehicleColumns: readonly Column<ReportVehicle>[] = [
    { header: 'Vehicle', cell: (v) => v.plate },
    { header: 'Delivered', cell: (v) => (v.delivered ? 'Yes' : v.withdrawn ? 'Withdrawn' : 'No') },
    { header: 'Boxes', cell: (v) => v.boxes },
    {
      header: 'By outcome',
      cell: (v) =>
        v.byOutcome
          .map((o) => `${o.outcome}: ${String(o.boxes)} (${gbp(o.valueGbp)})`)
          .join('; ') || 'No boxes',
    },
    { header: 'Delivered value', cell: (v) => gbp(v.deliveredValueGbp) },
    { header: 'Vehicle value', cell: (v) => (v.valueGbp === null ? '—' : gbp(v.valueGbp)) },
  ];

  return (
    <section className="report">
      <style>{`@media print { nav, .report__actions { display: none !important; } .report { margin: 0; } }`}</style>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>Convoy #{report.convoyId} report</h1>
        <span className="report__actions" style={{ display: 'flex', gap: 'var(--space-3)' }}>
          <LinkButton to={`/convoys/${String(report.convoyId)}`} variant="secondary">
            Back to convoy
          </LinkButton>
          <Button
            type="button"
            onClick={() => {
              window.print();
            }}
          >
            Print
          </Button>
        </span>
      </header>

      <p>
        Arrived {when(report.arrivedAt)}.{' '}
        {report.closedAt ? `Closed ${when(report.closedAt)}.` : 'Not closed yet.'}
      </p>

      <section aria-label="Totals">
        <h2>Totals</h2>
        <dl>
          <dt>Boxes</dt>
          <dd>{report.totals.boxes}</dd>
          <dt>Boxes delivered</dt>
          <dd>{report.totals.boxesDelivered}</dd>
          <dt>Boxes not delivered</dt>
          <dd>{report.totals.boxesNotDelivered}</dd>
          <dt>Boxes with no outcome</dt>
          <dd>{report.totals.boxesUndecided}</dd>
          <dt>Value delivered</dt>
          <dd>{gbp(report.totals.deliveredValueGbp)}</dd>
          <dt>Vehicle value handed over</dt>
          <dd>{gbp(report.totals.vehicleValueGbp)}</dd>
        </dl>
      </section>

      <section aria-label="Costs">
        <h2>Expected against actual costs</h2>
        {budget && budget.lines.length > 0 ? (
          <DataTable
            caption="Costs by type"
            columns={[
              { header: 'Type', cell: (line) => line.type },
              {
                header: 'Expected',
                cell: (line) => (line.plannedGbp === null ? 'No budget' : gbp(line.plannedGbp)),
              },
              { header: 'Actual', cell: (line) => gbp(line.actualGbp) },
              { header: '', cell: (line) => (line.overBudget ? 'Over budget' : '') },
            ]}
            rows={budget.lines}
            rowKey={(line) => line.type}
            emptyMessage="No costs."
          />
        ) : (
          <p role="status">No budget or costs were recorded.</p>
        )}
        {budget ? (
          <p>
            Expected {gbp(budget.plannedTotalGbp)}; actual {gbp(budget.actualTotalGbp)}.
          </p>
        ) : null}
      </section>

      <section aria-label="Vehicles">
        <h2>Vehicles and boxes</h2>
        <DataTable
          caption="Vehicles"
          columns={vehicleColumns}
          rows={report.vehicles}
          rowKey={(v) => v.vin}
          emptyMessage="No vehicles."
        />
      </section>

      <section aria-label="Receivers">
        <h2>Receivers</h2>
        {report.vehicles.flatMap((v) => v.receivers).length === 0 ? (
          <p role="status">No boxes were allocated.</p>
        ) : (
          <ul>
            {report.vehicles.flatMap((vehicle) =>
              vehicle.receivers.map((receiver) => (
                <li key={`${vehicle.vin}-${receiver.organisation}-${receiver.region}`}>
                  {receiver.organisation}
                  {receiver.region ? `, ${receiver.region}` : ''} — {receiver.boxesDelivered} of{' '}
                  {receiver.boxes} boxes delivered ({vehicle.plate})
                </li>
              )),
            )}
          </ul>
        )}
      </section>
    </section>
  );
}
