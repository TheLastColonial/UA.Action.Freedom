import type { JSX, ReactNode } from 'react';
import { Link } from 'react-router-dom';

import './Button.css';

interface LinkButtonProps {
  to: string;
  children: ReactNode;
}

export function LinkButton({ to, children }: LinkButtonProps): JSX.Element {
  return (
    <Link to={to} className="link-button">
      {children}
    </Link>
  );
}
