import type { JSX } from 'react';
import { NavLink } from 'react-router-dom';

import { useAuth } from '../auth/useAuth';
import './NavSidebar.css';
import { NAV_HOME, NAV_SECTIONS } from './navModel';

const linkClass = ({ isActive }: { isActive: boolean }) =>
  isActive ? 'nav-sidebar__link nav-sidebar__link--active' : 'nav-sidebar__link';

// Dashboard is the root of the tree; the sections a user may read hang off it one level down.
export function NavSidebar(): JSX.Element {
  const auth = useAuth();
  const sections = NAV_SECTIONS.filter(
    (entry) => entry.policy === undefined || auth.hasPolicy(entry.policy),
  );

  return (
    <nav aria-label="Sections" className="nav-sidebar">
      <ul className="nav-sidebar__list">
        <li>
          <NavLink to={NAV_HOME.to} end className={linkClass}>
            {NAV_HOME.label}
          </NavLink>
          {sections.length > 0 ? (
            <ul className="nav-sidebar__list nav-sidebar__list--nested">
              {sections.map((entry) => (
                <li key={entry.to}>
                  <NavLink to={entry.to} className={linkClass}>
                    {entry.label}
                  </NavLink>
                </li>
              ))}
            </ul>
          ) : null}
        </li>
      </ul>
    </nav>
  );
}
