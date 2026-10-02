import type { JSX } from 'react';

import { useMe } from '../api/me';

/**
 * "You will sign as …": an attestation is made in the name of the caller's linked volunteer, so
 * there is nothing to pick. A login no Administrator has linked cannot sign.
 */
export function SigningAs(): JSX.Element | null {
  const me = useMe();

  if (me.isPending || me.isError) {
    return null;
  }
  if (me.data.personId === null) {
    return (
      <p role="alert" className="field__error">
        Your login is not linked to a volunteer, so you cannot sign this. Ask an Administrator to
        link it.
      </p>
    );
  }

  return (
    <p>
      You will sign as <strong>{me.data.displayName}</strong>.
    </p>
  );
}

/** True once the caller's login is known to be linked to a volunteer. */
export function useCanSign(): boolean {
  const me = useMe();
  return me.isSuccess && me.data.personId !== null;
}
