import type { JSX } from 'react';
import { Link } from 'react-router-dom';

import { useAuth } from '../auth/useAuth';
import { NAV } from '../components/navModel';

export function Dashboard(): JSX.Element {
  const auth = useAuth();
  const sections = NAV.filter(
    (entry) => entry.to !== '/' && (entry.policy === undefined || auth.hasPolicy(entry.policy)),
  );

  return (
    <section>
      <h1>Dashboard</h1>
      <p>Signed in as {auth.sub ?? 'unknown'}.</p>
      <ul>
        {sections.map((entry) => (
          <li key={entry.to}>
            <Link to={entry.to}>{entry.label}</Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
