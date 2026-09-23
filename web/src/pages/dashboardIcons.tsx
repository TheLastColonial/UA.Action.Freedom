import type { JSX, ReactNode } from 'react';

export interface IconProps {
  readonly className?: string;
}

// One drawing style for the whole set: a 24px grid, 1.75 round strokes in currentColor (the
// brand blue), and one flat yellow accent per icon. Accents are drawn first so the outline sits
// on top of them.
function Icon({
  className,
  children,
}: {
  readonly className?: string | undefined;
  readonly children: ReactNode;
}): JSX.Element {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {children}
    </svg>
  );
}

const ACCENT = 'var(--color-brand-yellow)';

/** A donated van: cargo body, cab and a yellow windscreen. */
export function VehicleIcon({ className }: IconProps): JSX.Element {
  return (
    <Icon className={className}>
      <path d="M15.5 10.5h1.8l2.1 2.8h-3.9Z" fill={ACCENT} stroke="none" />
      <path d="M4.5 17H3a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h11v11" />
      <path d="M14 9h3.6l3.4 4.5V16a1 1 0 0 1-1 1h-.5" />
      <path d="M8.5 17h7" />
      <circle cx="6.5" cy="17" r="2" />
      <circle cx="17.5" cy="17" r="2" />
    </Icon>
  );
}

/** Volunteers: two people, the one in front highlighted. */
export function PeopleIcon({ className }: IconProps): JSX.Element {
  return (
    <Icon className={className}>
      <circle cx="9" cy="8" r="3.25" fill={ACCENT} />
      <path d="M3 20a6 6 0 0 1 12 0" />
      <path d="M16 4.9a3.25 3.25 0 0 1 0 6.2" />
      <path d="M17.5 14.3A6 6 0 0 1 21 20" />
    </Icon>
  );
}

/** Convoys: a winding road from a yellow start to the destination. */
export function RouteIcon({ className }: IconProps): JSX.Element {
  return (
    <Icon className={className}>
      <circle cx="5" cy="18" r="2" fill={ACCENT} />
      <path d="M7 18h8.5a3.5 3.5 0 0 0 0-7h-7a3.5 3.5 0 0 1 0-7H17" />
      <path d="M19 2.5v6" />
      <path d="M19 2.5h3l-1 1.5 1 1.5h-3" />
    </Icon>
  );
}

/** Boxes: a packed carton with yellow tape over the lid and down the side. */
export function BoxIcon({ className }: IconProps): JSX.Element {
  return (
    <Icon className={className}>
      <path d="M7.75 5.25 16.25 9.75v9" stroke={ACCENT} strokeWidth="2.5" />
      <path d="M12 3 20.5 7.5v9L12 21l-8.5-4.5v-9Z" />
      <path d="M3.5 7.5 12 12l8.5-4.5" />
      <path d="M12 12v9" />
    </Icon>
  );
}

/** Manifests: a clipboard with a checked list. */
export function DocumentIcon({ className }: IconProps): JSX.Element {
  return (
    <Icon className={className}>
      <path d="M8 4H6.5A1.5 1.5 0 0 0 5 5.5v14A1.5 1.5 0 0 0 6.5 21h11a1.5 1.5 0 0 0 1.5-1.5v-14A1.5 1.5 0 0 0 17.5 4H16" />
      <rect x="8" y="2.5" width="8" height="3.5" rx="1" fill={ACCENT} />
      <path d="m8.5 11 1.25 1.25L12 10" />
      <path d="M13.5 11.25H16" />
      <path d="m8.5 16 1.25 1.25L12 15" />
      <path d="M13.5 16.25H16" />
    </Icon>
  );
}

/** Receivers: the delivery point, a pin with a yellow centre. */
export function MapPinIcon({ className }: IconProps): JSX.Element {
  return (
    <Icon className={className}>
      <path d="M12 21.5s-7-6.1-7-11.5a7 7 0 0 1 14 0c0 5.4-7 11.5-7 11.5Z" />
      <circle cx="12" cy="10" r="2.75" fill={ACCENT} />
    </Icon>
  );
}

/** Locations: a depot with its storage bays, one of them filled. */
export function WarehouseIcon({ className }: IconProps): JSX.Element {
  return (
    <Icon className={className}>
      <rect x="7" y="12" width="5" height="4" fill={ACCENT} stroke="none" />
      <path d="M3 20V9l9-5 9 5v11" />
      <path d="M2 20h20" />
      <path d="M7 20v-8h10v8" />
      <path d="M7 16h10" />
      <path d="M12 12v8" />
    </Icon>
  );
}
