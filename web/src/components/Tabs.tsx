import type { JSX, ReactNode } from 'react';
import { useRef } from 'react';

import './Tabs.css';

interface TabSpec<T extends string> {
  id: T;
  label: string;
}

interface TabsProps<T extends string> {
  label: string;
  tabs: readonly TabSpec<T>[];
  active: T;
  onChange: (id: T) => void;
}

export function Tabs<T extends string>({
  label,
  tabs,
  active,
  onChange,
}: TabsProps<T>): JSX.Element {
  const tabRefs = useRef<Record<string, HTMLButtonElement | null>>({});

  const handleKeyDown = (event: React.KeyboardEvent) => {
    const currentIndex = tabs.findIndex((tab) => tab.id === active);
    let nextIndex: number | null = null;

    if (event.key === 'ArrowRight') {
      event.preventDefault();
      nextIndex = (currentIndex + 1) % tabs.length;
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
    } else if (event.key === 'Home') {
      event.preventDefault();
      nextIndex = 0;
    } else if (event.key === 'End') {
      event.preventDefault();
      nextIndex = tabs.length - 1;
    }

    if (nextIndex !== null) {
      const nextTab = tabs[nextIndex];
      if (nextTab) {
        onChange(nextTab.id);
        setTimeout(() => {
          tabRefs.current[nextTab.id]?.focus();
        }, 0);
      }
    }
  };

  return (
    <div
      role="tablist"
      aria-label={label}
      className="tabs__list"
      onKeyDown={handleKeyDown}
      tabIndex={-1}
    >
      {tabs.map((tab) => (
        <button
          key={tab.id}
          ref={(el) => {
            if (el) {
              tabRefs.current[tab.id] = el;
            }
          }}
          role="tab"
          id={`tab-${tab.id}`}
          aria-selected={active === tab.id}
          aria-controls={`tabpanel-${tab.id}`}
          tabIndex={active === tab.id ? 0 : -1}
          className={`tabs__tab ${active === tab.id ? 'tabs__tab--active' : ''}`}
          onClick={() => {
            onChange(tab.id);
          }}
          type="button"
        >
          {tab.label}
        </button>
      ))}
    </div>
  );
}

interface TabPanelProps {
  id: string;
  children: ReactNode;
}

export function TabPanel({ id, children }: TabPanelProps): JSX.Element {
  return (
    <div role="tabpanel" id={`tabpanel-${id}`} aria-labelledby={`tab-${id}`} tabIndex={0}>
      {children}
    </div>
  );
}
