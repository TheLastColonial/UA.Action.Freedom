import type { JSX } from 'react';
import { Link, Outlet } from 'react-router-dom';

import { useAuth } from '../auth/useAuth';
import './AppShell.css';
import { ColdStartIndicator } from './ColdStartIndicator';
import { NavSidebar } from './NavSidebar';

export function AppShell(): JSX.Element {
  const auth = useAuth();

  return (
    <div className="app-shell">
      <ColdStartIndicator />
      <header className="app-shell__header">
        <Link to="/" className="app-shell__brand">
          UA Action Freedom
        </Link>
        <div className="app-shell__identity">
          <span className="app-shell__roles">{auth.roles.join(', ') || 'no roles'}</span>
          <button type="button" onClick={auth.signOut}>
            Sign out
          </button>
        </div>
      </header>
      <div className="app-shell__body">
        <aside className="app-shell__sidebar">
          <NavSidebar />
        </aside>
        <main className="app-shell__main">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
