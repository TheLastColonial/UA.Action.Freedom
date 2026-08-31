import type { JSX } from 'react';
import { useEffect, useRef, useState } from 'react';

import { onSlowRequest } from '../api/slowRequestSignal';
import './ColdStartIndicator.css';

const VISIBLE_FOR_MS = 8000;

export function ColdStartIndicator(): JSX.Element | null {
  const [visible, setVisible] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    const unsubscribe = onSlowRequest(() => {
      setVisible(true);
      if (timer.current) {
        clearTimeout(timer.current);
      }
      timer.current = setTimeout(() => {
        setVisible(false);
      }, VISIBLE_FOR_MS);
    });

    return () => {
      unsubscribe();
      if (timer.current) {
        clearTimeout(timer.current);
      }
    };
  }, []);

  if (!visible) {
    return null;
  }

  return (
    <div role="status" className="cold-start-banner">
      Waking the server — this can take up to a minute after a quiet period.
    </div>
  );
}
