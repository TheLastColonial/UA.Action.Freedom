import type { JSX } from 'react';
import { Link } from 'react-router-dom';

import { useMe } from '../../api/me';

/**
 * A Convoy Leader's way in. The role is derived by the API from an open assignment and is never in the token, so the
 * dashboard cannot offer it by role: it asks who the caller leads (GET /me) and links the checklist of each such convoy.
 */
export function LeaderChecklistLinks(): JSX.Element | null {
  const me = useMe();
  const led = me.data?.ledConvoyIds ?? [];

  if (led.length === 0) {
    return null;
  }

  return (
    <nav aria-label="Your convoys">
      <h2>Your convoy</h2>
      <ul>
        {led.map((convoyId) => (
          <li key={convoyId}>
            <Link to={`/convoys/${String(convoyId)}/checklist`}>
              Checklist for convoy {convoyId}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
