import type { JSX, ReactNode } from 'react';

import './FormCard.css';

interface FormCardProps {
  title: string;
  children: ReactNode;
}

export function FormCard({ title, children }: FormCardProps): JSX.Element {
  return (
    <fieldset className="form-card">
      <legend className="form-card__legend">{title}</legend>
      {children}
    </fieldset>
  );
}
