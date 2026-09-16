import type { JSX } from 'react';

import { useAuth } from '../auth/useAuth';
import './Dashboard.css';
import { DashboardCard } from './DashboardCard';
import { DASHBOARD_CARD_ENTRIES } from './dashboardCards';

export function Dashboard(): JSX.Element {
  const auth = useAuth();
  const entries = DASHBOARD_CARD_ENTRIES.filter((entry) => auth.hasPolicy(entry.policy));

  return (
    <section>
      <h1>Dashboard</h1>
      <div className="dashboard-grid">
        {entries.map((entry) => (
          <DashboardCard key={entry.to} entry={entry} />
        ))}
      </div>
    </section>
  );
}
