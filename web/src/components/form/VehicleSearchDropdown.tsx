import type { JSX, KeyboardEvent } from 'react';
import { useId, useMemo, useState } from 'react';

import { useVehicles } from '../../api/vehicles';
import type { VehicleReadModel } from '../../api/schemas/vehicles';
import './VehicleSearchDropdown.css';

interface VehicleSearchDropdownProps {
  onSelect: (vin: string) => void;
  disabled?: boolean;
  excludeVins?: readonly string[];
}

// The API clamps a page at 200. A fleet larger than that needs a server-side search here.
const FLEET_PAGE = { page: 1, pageSize: 200 } as const;

function matches(vehicle: VehicleReadModel, search: string): boolean {
  return [vehicle.vin, vehicle.plate, vehicle.brand, vehicle.model].some(
    (field) => field?.toLowerCase().includes(search) ?? false,
  );
}

/**
 * Picks a vehicle for a convoy's truck list. Only vehicles that have passed their servicing
 * inspection are offered — the API refuses any other with a 409, so offering them would only
 * invite the error. Follows the ARIA combobox pattern: arrows move, Enter chooses, Escape closes.
 */
export function VehicleSearchDropdown({
  onSelect,
  disabled = false,
  excludeVins = [],
}: VehicleSearchDropdownProps): JSX.Element {
  const [search, setSearch] = useState('');
  const [isOpen, setIsOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const id = useId();
  const listId = `${id}-list`;
  const optionId = (index: number) => `${id}-option-${String(index)}`;

  const query = useVehicles(FLEET_PAGE);

  const eligible = useMemo(() => {
    const excluded = new Set(excludeVins);
    return (query.data ?? []).filter(
      (vehicle) =>
        vehicle.inspectionStatus === 'Passed' &&
        vehicle.handedOverAt === null &&
        !excluded.has(vehicle.vin),
    );
  }, [query.data, excludeVins]);

  const term = search.trim().toLowerCase();
  const options = term ? eligible.filter((vehicle) => matches(vehicle, term)) : eligible;

  const open = () => {
    setIsOpen(true);
  };
  const close = () => {
    setIsOpen(false);
    setActive(-1);
  };
  const choose = (vin: string) => {
    onSelect(vin);
    setSearch('');
    close();
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape') {
      close();
      return;
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      open();
      const step = event.key === 'ArrowDown' ? 1 : -1;
      setActive((current) => Math.min(Math.max(current + step, 0), options.length - 1));
      return;
    }
    const chosen = options[active];
    if (event.key === 'Enter' && isOpen && chosen) {
      event.preventDefault();
      choose(chosen.vin);
    }
  };

  if (query.isError) {
    return <p role="alert">The vehicle list could not be loaded. {query.error.message}</p>;
  }

  const emptyMessage = term
    ? 'No passed vehicle matches'
    : 'No vehicles have passed inspection yet';
  const activeOption = isOpen && options[active] ? optionId(active) : undefined;

  return (
    <div className="vehicle-search-dropdown">
      <label htmlFor={id} className="field__label">
        Vehicle
      </label>
      <p className="field__hint">Only vehicles that have passed their inspection are listed.</p>
      <div className="vehicle-search-dropdown__input-wrapper">
        <input
          id={id}
          type="text"
          role="combobox"
          aria-expanded={isOpen}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={activeOption}
          placeholder="Search by VIN, plate, brand, or model…"
          value={search}
          onChange={(event) => {
            setSearch(event.target.value);
            setActive(-1);
            open();
          }}
          onFocus={open}
          onClick={open}
          onKeyDown={handleKeyDown}
          disabled={disabled || query.isPending}
          autoComplete="off"
          className="vehicle-search-dropdown__input"
        />
      </div>

      {isOpen ? (
        <ul
          id={listId}
          className="vehicle-search-dropdown__list"
          role="listbox"
          aria-labelledby={id}
        >
          {query.isPending ? (
            <li className="vehicle-search-dropdown__item vehicle-search-dropdown__item--status">
              Loading vehicles…
            </li>
          ) : options.length === 0 ? (
            <li className="vehicle-search-dropdown__item vehicle-search-dropdown__item--status">
              {emptyMessage}
            </li>
          ) : (
            options.map((vehicle, index) => (
              <li
                key={vehicle.vin}
                id={optionId(index)}
                role="option"
                aria-selected={index === active}
                className="vehicle-search-dropdown__item"
                onMouseDown={(event) => {
                  event.preventDefault();
                  choose(vehicle.vin);
                }}
              >
                <span className="vehicle-search-dropdown__vin">{vehicle.vin}</span>
                <span className="vehicle-search-dropdown__plate">{vehicle.plate}</span>
                {vehicle.brand ? (
                  <span className="vehicle-search-dropdown__model">
                    {[vehicle.brand, vehicle.model].filter(Boolean).join(' ')}
                  </span>
                ) : null}
              </li>
            ))
          )}
        </ul>
      ) : null}
    </div>
  );
}
