import type { JSX } from 'react';
import { Link } from 'react-router-dom';

import { LinkButton } from '../components/Button';
import { Gate } from '../components/Gate';
import './DashboardCard.css';
import type { DashboardCardEntry } from './dashboardCards';

interface DashboardCardProps {
  readonly entry: DashboardCardEntry;
}

export function DashboardCard({ entry }: DashboardCardProps): JSX.Element {
  const Icon = entry.icon;

  return (
    <div className="dashboard-card">
      <Icon className="dashboard-card__icon" />
      <Link to={entry.to} className="dashboard-card__link">
        {entry.label}
      </Link>
      <Gate policy={entry.actionPolicy}>
        <div className="dashboard-card__action">
          <LinkButton to={entry.actionTo}>{entry.actionLabel}</LinkButton>
        </div>
      </Gate>
    </div>
  );
}
