import type { JSX } from 'react';
import { useEffect, useId, useRef, useState } from 'react';

import './ReasonModal.css';

interface ReasonModalProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  submitting: boolean;
  errorMessage?: string | undefined;
  onSubmit: (reason: string) => void;
}

export function ReasonModal({
  isOpen,
  onOpenChange,
  submitting,
  errorMessage,
  onSubmit,
}: ReasonModalProps): JSX.Element | null {
  const [reason, setReason] = useState('');
  const [touchedEmpty, setTouchedEmpty] = useState(false);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const titleId = useId();

  useEffect(() => {
    if (isOpen) {
      setReason('');
      setTouchedEmpty(false);
      textareaRef.current?.focus();
    }
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) {
      return;
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onOpenChange(false);
      }
    };
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('keydown', onKey);
    };
  }, [isOpen, onOpenChange]);

  if (!isOpen) {
    return null;
  }

  const submit = () => {
    if (reason.trim().length === 0) {
      setTouchedEmpty(true);
      return;
    }
    onSubmit(reason.trim());
  };

  return (
    <div
      className="reason-modal__overlay"
      role="presentation"
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          onOpenChange(false);
        }
      }}
    >
      <div className="reason-modal" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <h2 id={titleId}>Reveal delivery detail</h2>
        <p className="reason-modal__warning">This access is recorded in the receiver access log.</p>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            submit();
          }}
        >
          <div className="field">
            <label className="field__label" htmlFor="receiver-detail-reason">
              Reason for viewing
            </label>
            <textarea
              id="receiver-detail-reason"
              ref={textareaRef}
              value={reason}
              onChange={(event) => {
                setReason(event.target.value);
                setTouchedEmpty(false);
              }}
            />
            {touchedEmpty ? (
              <p className="field__error" role="alert">
                State why you need to see this.
              </p>
            ) : null}
            {errorMessage ? (
              <p className="field__error" role="alert">
                {errorMessage}
              </p>
            ) : null}
          </div>
          <div style={{ display: 'flex', gap: 'var(--space-3)' }}>
            <button
              type="button"
              onClick={() => {
                onOpenChange(false);
              }}
            >
              Cancel
            </button>
            <button type="submit" disabled={submitting}>
              {submitting ? 'Revealing…' : 'Reveal detail'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
