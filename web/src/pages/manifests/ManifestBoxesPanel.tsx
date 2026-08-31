import type { JSX } from 'react';
import { useState } from 'react';

import { useAttachManifestBox, useDetachManifestBox, useManifestBoxes } from '../../api/manifests';
import { ApiDomainProblem } from '../../api/problem';
import type { ManifestBoxReadModel } from '../../api/schemas/manifests';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { PageSkeleton } from '../../components/PageSkeleton';

interface ManifestBoxesPanelProps {
  manifestId: string;
  frozen: boolean;
}

export function ManifestBoxesPanel({ manifestId, frozen }: ManifestBoxesPanelProps): JSX.Element {
  const query = useManifestBoxes(manifestId);
  const attach = useAttachManifestBox(manifestId);
  const detach = useDetachManifestBox(manifestId);
  const [boxId, setBoxId] = useState('');

  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The manifest cargo could not be loaded.</p>;
  }

  const rows: readonly ManifestBoxReadModel[] = 'parentMissing' in query.data ? [] : query.data;
  const problemMessage = (error: unknown) =>
    error instanceof ApiDomainProblem ? (error.detail ?? error.message) : undefined;
  const message = problemMessage(attach.error) ?? problemMessage(detach.error);

  const columns: readonly Column<ManifestBoxReadModel>[] = [
    { header: 'Box', cell: (b) => `#${b.boxId}` },
    { header: 'Weight (kg)', cell: (b) => b.weightKg },
    { header: 'Validated', cell: (b) => (b.validated ? 'Yes' : 'No') },
    {
      header: '',
      cell: (b) => (
        <button
          type="button"
          disabled={frozen || detach.isPending}
          onClick={() => {
            detach.mutate(b.boxId);
          }}
        >
          Remove
        </button>
      ),
    },
  ];

  return (
    <div>
      <h2>Cargo</h2>
      {frozen ? <p role="status">Frozen — cargo can no longer be changed.</p> : null}
      {message ? (
        <p role="alert" className="field__error">
          {message}
        </p>
      ) : null}

      <DataTable
        caption="Boxes on this manifest"
        columns={columns}
        rows={rows}
        rowKey={(b) => String(b.boxId)}
        emptyMessage="No boxes on this manifest yet."
      />

      {!frozen ? (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            const parsed = Number(boxId.trim());
            if (Number.isInteger(parsed) && parsed > 0) {
              attach.mutate(parsed, {
                onSuccess: () => {
                  setBoxId('');
                },
              });
            }
          }}
          style={{ display: 'flex', gap: 'var(--space-2)', alignItems: 'end' }}
        >
          <label style={{ display: 'flex', flexDirection: 'column' }}>
            Box id to add
            <input
              inputMode="numeric"
              value={boxId}
              onChange={(event) => {
                setBoxId(event.target.value);
              }}
            />
          </label>
          <button type="submit" disabled={attach.isPending}>
            Add box
          </button>
        </form>
      ) : null}
    </div>
  );
}
