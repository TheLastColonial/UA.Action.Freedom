import type { ComponentPropsWithoutRef, JSX, ReactNode } from 'react';
import { Link } from 'react-router-dom';

import './Button.css';

export type ButtonVariant = 'primary' | 'secondary' | 'danger';

interface ButtonProps extends Omit<ComponentPropsWithoutRef<'button'>, 'className'> {
  variant?: ButtonVariant;
}

export function Button({
  variant = 'primary',
  type = 'button',
  ...rest
}: ButtonProps): JSX.Element {
  return <button type={type} className={`btn btn--${variant}`} {...rest} />;
}

interface LinkButtonProps {
  to: string;
  variant?: ButtonVariant;
  children: ReactNode;
}

export function LinkButton({ to, variant = 'primary', children }: LinkButtonProps): JSX.Element {
  return (
    <Link to={to} className={`btn btn--${variant}`}>
      {children}
    </Link>
  );
}
