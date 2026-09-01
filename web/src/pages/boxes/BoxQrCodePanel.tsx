import type { JSX } from 'react';

import { useBoxLabel, useBoxQrCode, useIssueBoxQrCode, useRevokeBoxQrCode } from '../../api/boxes';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import './BoxQrCodePanel.css';

interface BoxQrCodePanelProps {
  boxId: number;
}

export function BoxQrCodePanel({ boxId }: BoxQrCodePanelProps): JSX.Element {
  const qrCode = useBoxQrCode(boxId);
  const issue = useIssueBoxQrCode(boxId);
  const revoke = useRevokeBoxQrCode(boxId);

  const active = qrCode.data ?? null;
  const label = useBoxLabel(boxId, active !== null);

  if (qrCode.isPending) {
    return (
      <div className="qr-panel">
        <h2>QR label</h2>
        <PageSkeleton />
      </div>
    );
  }

  return (
    <div className="qr-panel">
      <h2>QR label</h2>

      {qrCode.isError ? <p role="alert">The QR label could not be loaded.</p> : null}

      {active === null ? (
        <>
          <p>This box has no QR label.</p>
          <Gate policy="boxes:write" fallback={<p>Ask a dispatcher or loader to issue one.</p>}>
            <button
              type="button"
              disabled={issue.isPending}
              onClick={() => {
                issue.mutate();
              }}
            >
              Issue label
            </button>
          </Gate>
        </>
      ) : (
        <>
          <p>
            Label issued <time dateTime={active.issuedAt}>{active.issuedAt.slice(0, 10)}</time>.
          </p>

          {label.data !== undefined ? (
            <div className="qr-panel__print">
              <img
                className="qr-panel__label"
                src={`data:image/svg+xml,${encodeURIComponent(label.data)}`}
                alt={`QR label for box ${String(boxId)}`}
              />
            </div>
          ) : null}

          <div className="qr-panel__actions">
            <button
              type="button"
              disabled={label.data === undefined}
              onClick={() => {
                window.print();
              }}
            >
              Print label
            </button>
            <Gate policy="boxes:write">
              <button
                type="button"
                disabled={issue.isPending}
                onClick={() => {
                  issue.mutate();
                }}
              >
                Reissue label
              </button>
              <button
                type="button"
                disabled={revoke.isPending}
                onClick={() => {
                  revoke.mutate();
                }}
              >
                Revoke label
              </button>
            </Gate>
          </div>

          <p className="qr-panel__note">
            The label carries only the box number — never the receiver or destination.
          </p>
        </>
      )}

      {issue.isError ? <p role="alert">The label could not be issued.</p> : null}
      {revoke.isError ? <p role="alert">The label could not be revoked.</p> : null}
    </div>
  );
}
