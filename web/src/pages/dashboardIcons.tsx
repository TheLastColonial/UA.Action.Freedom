import type { JSX } from 'react';

export interface IconProps {
  readonly className?: string;
}

export function VehicleIcon({ className }: IconProps): JSX.Element {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      aria-hidden="true"
    >
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M3 16V11l2-5h10l3 5h3v5h-2M3 16h2m0 0a2 2 0 1 0 4 0m-4 0h10m0 0a2 2 0 1 0 4 0m0 0h2"
      />
    </svg>
  );
}

export function PeopleIcon({ className }: IconProps): JSX.Element {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      aria-hidden="true"
    >
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M9 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm-6 9c0-3.3 2.7-6 6-6s6 2.7 6 6M17 11a3 3 0 1 0 0-6M15.5 14c2.6.4 4.5 2.6 4.5 5.5"
      />
    </svg>
  );
}

export function RouteIcon({ className }: IconProps): JSX.Element {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      aria-hidden="true"
    >
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M5 19c2 0 2-4 4-4s2 4 4 4 2-4 4-4 2-4 4-4"
      />
      <circle cx="5" cy="19" r="1.5" />
      <circle cx="19" cy="7" r="1.5" />
    </svg>
  );
}

export function BoxIcon({ className }: IconProps): JSX.Element {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      aria-hidden="true"
    >
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M3 8l9-5 9 5-9 5-9-5Zm0 0v8l9 5m0-13v13m0-13l9-5m0 5v8l-9 5"
      />
    </svg>
  );
}

export function DocumentIcon({ className }: IconProps): JSX.Element {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      aria-hidden="true"
    >
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M7 3h7l4 4v14H7V3Zm7 0v4h4M9 12h6M9 16h6M9 8h2"
      />
    </svg>
  );
}

export function MapPinIcon({ className }: IconProps): JSX.Element {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      aria-hidden="true"
    >
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M12 21s7-6.6 7-12a7 7 0 1 0-14 0c0 5.4 7 12 7 12Z"
      />
      <circle cx="12" cy="9" r="2.5" />
    </svg>
  );
}

export function WarehouseIcon({ className }: IconProps): JSX.Element {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      aria-hidden="true"
    >
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M3 10.5 12 4l9 6.5V20a1 1 0 0 1-1 1h-4v-6H9v6H4a1 1 0 0 1-1-1V10.5Z"
      />
    </svg>
  );
}
