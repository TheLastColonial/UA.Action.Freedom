import type { JSX } from 'react';

import { useAcceptGoodsList } from '../../api/outcomes';
import { ApiDomainProblem } from '../../api/problem';
import { Button } from '../../components/Button';
import { Gate } from '../../components/Gate';

interface AcceptGoodsListButtonProps {
  convoyId: number;
  vin: string;
  receiverRef: string;
}

/**
 * Acceptance is recorded once per Ukrainian goods list (O24): every delivered box for that Receiver on this vehicle becomes
 * accepted. The Dispatcher records it for the Convoy Leader until scoped permissions exist.
 */
export function AcceptGoodsListButton({
  convoyId,
  vin,
  receiverRef,
}: AcceptGoodsListButtonProps): JSX.Element {
  const accept = useAcceptGoodsList(convoyId, vin);
  const message =
    accept.error instanceof ApiDomainProblem
      ? (accept.error.detail ?? accept.error.message)
      : undefined;

  return (
    <Gate policy="convoys:write">
      <Button
        type="button"
        variant="secondary"
        disabled={accept.isPending}
        onClick={() => {
          accept.mutate(receiverRef);
        }}
      >
        Accept goods list
      </Button>
      {accept.isSuccess ? <p role="status">Accepted.</p> : null}
      {message ? (
        <p role="alert" className="field__error">
          {message}
        </p>
      ) : null}
    </Gate>
  );
}
