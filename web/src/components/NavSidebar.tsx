import type { JSX } from 'react';
import { NavLink } from 'react-router-dom';

import { useAuth } from '../auth/useAuth';
import './NavSidebar.css';
import { NAV } from './navModel';

export function NavSidebar(): JSX.Element {
  const auth = useAuth();
  const entries = NAV.filter((entry) => entry.policy === undefined || auth.hasPolicy(entry.policy));

  return (
    <nav aria-label="Sections" className="nav-sidebar">
      <ul className="nav-sidebar__list">
        {entries.map((entry) => (
          <li key={entry.to}>
            <NavLink
              to={entry.to}
              end={entry.to === '/'}
              className={({ isActive }) =>
                isActive ? 'nav-sidebar__link nav-sidebar__link--active' : 'nav-sidebar__link'
              }
            >
              {entry.label}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
}
